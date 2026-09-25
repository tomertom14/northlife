using Microsoft.AspNetCore.DataProtection;
using NorthLife.Api.Identity;
using System.Text;

namespace NorthLife.Tests;

public sealed class TotpTests
{
    // RFC 6238 Appendix B, SHA-1 rows: 20-byte ASCII key "12345678901234567890", 8 digits.
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Matches_rfc_6238_test_vectors(long unixSeconds, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, Totp.Compute(RfcKey, step, digits: 8));
    }

    [Fact]
    public void Accepts_one_step_of_clock_drift_either_way_but_not_two()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);

        Assert.Equal(step - 1, Totp.Verify(RfcKey, Totp.Compute(RfcKey, step - 1), now, lastUsedStep: null));
        Assert.Equal(step + 1, Totp.Verify(RfcKey, Totp.Compute(RfcKey, step + 1), now, lastUsedStep: null));
        Assert.Null(Totp.Verify(RfcKey, Totp.Compute(RfcKey, step - 2), now, lastUsedStep: null));
    }

    [Fact]
    public void Rejects_a_code_from_an_already_used_step()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);
        var code = Totp.Compute(RfcKey, step);

        Assert.Equal(step, Totp.Verify(RfcKey, code, now, lastUsedStep: null));
        Assert.Null(Totp.Verify(RfcKey, code, now, lastUsedStep: step));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Rejects_malformed_codes(string code)
    {
        Assert.Null(Totp.Verify(RfcKey, code, DateTimeOffset.UtcNow, lastUsedStep: null));
    }

    [Fact]
    public void Provisioning_uri_carries_the_base32_secret_and_issuer()
    {
        var uri = Totp.ProvisioningUri("NorthLife", "owner@example.com", RfcKey);

        Assert.StartsWith("otpauth://totp/NorthLife:owner%40example.com?", uri);
        Assert.Contains($"secret={Base32.Encode(RfcKey)}", uri);
        Assert.Contains("issuer=NorthLife", uri);
    }
}

public sealed class Base32Tests
{
    // RFC 4648 §10 vectors (padding removed, as in otpauth secrets).
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encodes_and_decodes_rfc_4648_vectors(string plain, string encoded)
    {
        Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(plain)));
        Assert.Equal(plain, Encoding.ASCII.GetString(Base32.Decode(encoded)));
    }

    [Fact]
    public void Decoding_ignores_case_spaces_and_padding()
    {
        Assert.Equal("foobar", Encoding.ASCII.GetString(Base32.Decode("mzxw 6ytb oi======")));
    }

    [Fact]
    public void Rejects_characters_outside_the_alphabet()
    {
        Assert.Throws<FormatException>(() => Base32.Decode("MZ1W"));
    }
}

public sealed class SecureTokenTests
{
    [Fact]
    public void Link_tokens_are_unique_url_safe_and_stored_only_as_their_hash()
    {
        var (first, firstHash) = SecureTokens.NewLinkToken();
        var (second, _) = SecureTokens.NewLinkToken();

        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.Equal(SecureTokens.Hash(first), firstHash);
        Assert.Equal(64, firstHash.Length);
        Assert.NotEqual(first, firstHash);
    }

    [Fact]
    public void Recovery_codes_verify_regardless_of_case_hyphen_or_spaces()
    {
        var (code, hash) = SecureTokens.NewRecoveryCode();

        Assert.Matches("^[a-z2-9]{4}-[a-z2-9]{4}$", code);
        Assert.Equal(hash, SecureTokens.Hash(SecureTokens.NormalizeRecoveryCode($" {code.ToUpperInvariant().Replace("-", " ")} ")));
    }
}

public sealed class GoogleAccountResolverTests
{
    [Theory]
    [InlineData(true, true, false, GoogleSignInDecision.SignInLinked)]
    [InlineData(true, false, true, GoogleSignInDecision.SignInLinked)]
    [InlineData(false, true, true, GoogleSignInDecision.LinkExisting)]
    [InlineData(false, false, true, GoogleSignInDecision.RequireProfile)]
    [InlineData(false, true, false, GoogleSignInDecision.RejectUnverifiedEmail)]
    [InlineData(false, false, false, GoogleSignInDecision.RejectUnverifiedEmail)]
    public void Links_only_on_a_verified_email(bool linked, bool emailExists, bool verified, GoogleSignInDecision expected)
    {
        Assert.Equal(expected, GoogleAccountResolver.Decide(linked, emailExists, verified));
    }
}

public sealed class IdentityTicketTests
{
    private readonly IdentityTickets _tickets = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Mfa_ticket_round_trips_and_rejects_tampering()
    {
        var userId = Guid.CreateVersion7();
        var ticket = _tickets.IssueMfa(userId);

        Assert.Equal(userId, _tickets.ReadMfa(ticket));
        Assert.Null(_tickets.ReadMfa(ticket[..^2] + "xx"));
        Assert.Null(_tickets.ReadMfa("not-a-ticket"));
    }

    [Fact]
    public void Signup_ticket_cannot_be_used_as_an_mfa_ticket()
    {
        var ticket = _tickets.IssueSignup(new GoogleIdentity("sub-1", "a@example.com", true, "A"));

        Assert.Equal("sub-1", _tickets.ReadSignup(ticket)?.Subject);
        Assert.Null(_tickets.ReadMfa(ticket));
    }
}

public sealed class SecondFactorThrottleTests
{
    private static SecondFactorThrottle NewThrottle() =>
        new(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

    [Fact]
    public void Locks_after_the_maximum_number_of_failures()
    {
        var throttle = NewThrottle();
        var userId = Guid.NewGuid();
        for (var failure = 1; failure < SecondFactorThrottle.MaxFailures; failure++)
        {
            throttle.RecordFailure(userId);
            Assert.False(throttle.IsLocked(userId));
        }

        throttle.RecordFailure(userId);
        Assert.True(throttle.IsLocked(userId));
    }

    [Fact]
    public void Counts_each_account_separately_and_resets_on_success()
    {
        var throttle = NewThrottle();
        var attacked = Guid.NewGuid();
        var other = Guid.NewGuid();
        for (var failure = 0; failure < SecondFactorThrottle.MaxFailures; failure++) throttle.RecordFailure(attacked);

        Assert.True(throttle.IsLocked(attacked));
        Assert.False(throttle.IsLocked(other));
        throttle.Reset(attacked);
        Assert.False(throttle.IsLocked(attacked));
    }
}
