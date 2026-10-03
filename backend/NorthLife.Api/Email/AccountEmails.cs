using System.Net;
using Microsoft.Extensions.Options;

namespace NorthLife.Api.Email;

/// <summary>Hebrew account emails. Sending failures are logged, never surfaced to the caller.</summary>
public sealed class AccountEmails(
    IEmailSender sender,
    IOptions<EmailOptions> options,
    ILogger<AccountEmails> logger)
{
    public Task SendVerificationAsync(string to, string fullName, string token, CancellationToken cancellationToken) =>
        SendAsync(
            to,
            "אימות כתובת האימייל שלכם ב-NorthLife",
            fullName,
            "נשאר רק לאמת את כתובת האימייל כדי שתוכלו לשלוח אירועים לאישור.",
            "אימות האימייל",
            Link("/manage/verify-email", token),
            "הקישור תקף ל-24 שעות. אם לא נרשמתם ל-NorthLife, אפשר להתעלם מההודעה.",
            cancellationToken);

    public Task SendPasswordResetAsync(string to, string fullName, string token, CancellationToken cancellationToken) =>
        SendAsync(
            to,
            "איפוס הסיסמה שלכם ב-NorthLife",
            fullName,
            "התקבלה בקשה לאיפוס הסיסמה לחשבון שלכם.",
            "בחירת סיסמה חדשה",
            Link("/manage/reset-password", token),
            "הקישור תקף ל-30 דקות ולשימוש אחד. אם לא ביקשתם איפוס, הסיסמה הנוכחית נשארת בתוקף.",
            cancellationToken);

    private string Link(string path, string token) =>
        $"{options.Value.PublicBaseUrl.TrimEnd('/')}{path}?token={Uri.EscapeDataString(token)}";

    private async Task SendAsync(
        string to,
        string subject,
        string fullName,
        string lead,
        string action,
        string link,
        string footnote,
        CancellationToken cancellationToken)
    {
        var name = WebUtility.HtmlEncode(fullName);
        var href = WebUtility.HtmlEncode(link);
        var html = $$"""
            <!doctype html>
            <html lang="he" dir="rtl">
            <body style="margin:0;background:#f2f4f3;font-family:Arial,sans-serif;color:#1d2320">
              <div style="max-width:32rem;margin:0 auto;padding:2rem 1.25rem">
                <p style="font-size:1.75rem;font-weight:700;margin:0 0 1.5rem">NorthLife</p>
                <p style="font-size:1.05rem;margin:0 0 .5rem">שלום {{name}},</p>
                <p style="font-size:1.05rem;line-height:1.6;margin:0 0 1.5rem">{{WebUtility.HtmlEncode(lead)}}</p>
                <p style="margin:0 0 1.5rem">
                  <a href="{{href}}" style="display:inline-block;background:#1d2320;color:#f2f4f3;padding:.8rem 1.4rem;border-radius:.5rem;text-decoration:none;font-weight:700">{{WebUtility.HtmlEncode(action)}}</a>
                </p>
                <p style="font-size:.9rem;color:#56605b;line-height:1.6;margin:0">{{WebUtility.HtmlEncode(footnote)}}</p>
              </div>
            </body>
            </html>
            """;
        var text = $"שלום {fullName},\n\n{lead}\n\n{action}: {link}\n\n{footnote}";

        try
        {
            await sender.SendAsync(new EmailMessage(to, subject, html, text), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Sending the \"{Subject}\" email failed.", subject);
        }
    }
}
