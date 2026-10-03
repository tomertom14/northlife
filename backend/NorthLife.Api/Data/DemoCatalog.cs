using NorthLife.Api.Analytics;
using NorthLife.Api.Models;

namespace NorthLife.Api.Data;

public sealed record DemoEventSpec(EventCategory Category, string Title, string Detail, string[] Tags);

public sealed record DemoLocality(string Name, decimal Latitude, decimal Longitude);

/// <summary>One catalogue event placed in time and space; see <see cref="DemoCatalog.Schedule"/>.</summary>
public sealed record DemoEventPlan(
    DemoEventSpec Spec,
    DemoLocality Locality,
    string Venue,
    decimal Price,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    bool IsHighlighted);

/// <summary>
/// A reproducible demo catalogue for local testing and for the ranking and recommendation
/// experiments: six businesses, 64 upcoming events over two weeks in 14 northern localities, with
/// Hebrew titles, descriptions and tags that give the text model real vocabulary to work with.
/// </summary>
public static class DemoCatalog
{
    public const string EmailDomain = "@demo.northlife.local";

    public static readonly DemoLocality[] Localities =
    [
        new("קריית שמונה", 33.2073m, 35.5700m),
        new("צפת", 32.9646m, 35.4960m),
        new("טבריה", 32.7922m, 35.5312m),
        new("ראש פינה", 32.9690m, 35.5420m),
        new("קצרין", 32.9925m, 35.6906m),
        new("כרמיאל", 32.9136m, 35.2961m),
        new("נהריה", 33.0059m, 35.0943m),
        new("עכו", 32.9281m, 35.0818m),
        new("מטולה", 33.2769m, 35.5786m),
        new("מעלות־תרשיחא", 33.0167m, 35.2667m),
        new("תל חי", 33.2340m, 35.5790m),
        new("עפולה", 32.6078m, 35.2897m),
        new("בית שאן", 32.4973m, 35.4969m),
        new("נצרת", 32.6996m, 35.3035m),
    ];

    public static readonly (string FullName, string BusinessName, EventCategory[] Categories)[] Owners =
    [
        ("יעל מזרחי", "גליל לייב", [EventCategory.Music, EventCategory.Nightlife]),
        ("אורי ביטון", "טעמים מהצפון", [EventCategory.Food]),
        ("מיכל אברהם", "סדנאות בהר", [EventCategory.Workshops]),
        ("איתי שרון", "טבע וגליל", [EventCategory.Outdoors, EventCategory.Sports]),
        ("רוני חדד", "במה צפונית", [EventCategory.Culture]),
        ("שירה פרץ", "קהילה בצפון", [EventCategory.Other]),
    ];

    public static readonly Dictionary<EventCategory, string> CategoryColors = new()
    {
        [EventCategory.Music] = "#835bae",
        [EventCategory.Nightlife] = "#516cbd",
        [EventCategory.Food] = "#ae5528",
        [EventCategory.Outdoors] = "#2f8543",
        [EventCategory.Culture] = "#aa4d75",
        [EventCategory.Workshops] = "#996700",
        [EventCategory.Sports] = "#007faa",
        [EventCategory.Other] = "#6a746e",
    };

    private static readonly Dictionary<EventCategory, string[]> Venues = new()
    {
        [EventCategory.Music] = ["חאן הגליל", "בית הבירה", "מרכז המוזיקה", "גג הקצבייה", "חצר האבנים"],
        [EventCategory.Nightlife] = ["המחסן", "פאב השכונה", "בר הגג", "חוף הירדן", "מועדון הצפון"],
        [EventCategory.Food] = ["כרם הגליל", "שוק האיכרים", "מטבח הכפר", "מחלבת ההר", "יקב העמק"],
        [EventCategory.Workshops] = ["סטודיו חימר", "בית המלאכה", "מרכז היצירה", "סטודיו ההר", "הגלריה הקטנה"],
        [EventCategory.Outdoors] = ["חניון הנחל", "שער השמורה", "מרכז המבקרים", "תחנת האופניים", "נקודת התצפית"],
        [EventCategory.Culture] = ["גלריה עירונית", "תיאטרון הצפון", "בית התרבות", "הרחבה המרכזית", "הספרייה העירונית"],
        [EventCategory.Sports] = ["הפארק העירוני", "חוף הכנרת", "מגרש הקהילה", "מרכז הספורט", "שביל האופניים"],
        [EventCategory.Other] = ["מתנ״ס העיר", "החווה", "כיכר השוק", "מרכז הקהילה", "בית הקפה"],
    };

    public static readonly DemoEventSpec[] Events =
    [
        new(EventCategory.Music, "ערב ג׳אז על הגג", "טריו ג׳אז מקומי מנגן סטנדרטים ואלתורים מול נוף ההרים", ["ג׳אז", "הופעה חיה", "גג", "ערב"]),
        new(EventCategory.Music, "הופעה אקוסטית בחצר", "זמרת יוצרת עם גיטרה אקוסטית ושירים מקוריים בעברית", ["אקוסטי", "גיטרה", "הופעה חיה", "אינטימי"]),
        new(EventCategory.Music, "רוק בגליל", "שלוש להקות רוק צעירות מהצפון על במה אחת", ["רוק", "להקה", "הופעה חיה", "אנרגיה"]),
        new(EventCategory.Music, "מוזיקה ים־תיכונית בשקיעה", "עוד, כינור וכלי הקשה בשקיעה מול הנוף", ["ים תיכוני", "שקיעה", "עוד", "הופעה חיה"]),
        new(EventCategory.Music, "קונצרט קאמרי בכנסייה העתיקה", "רביעיית מיתרים מנגנת מוצרט ובטהובן באקוסטיקה של אבן עתיקה", ["קלאסי", "קאמרי", "כינור", "צ׳לו"]),
        new(EventCategory.Music, "ג׳אם פתוח לנגנים", "מביאים כלי ומצטרפים לבמה הפתוחה, כל הרמות מוזמנות", ["ג׳אם", "נגנים", "אלתור", "הופעה חיה"]),
        new(EventCategory.Music, "ערב פולק ושירי ארץ ישראל", "שירה בציבור עם מנחה וגיטרה, המילים על המסך", ["פולק", "שירה בציבור", "נוסטלגיה", "גיטרה"]),
        new(EventCategory.Music, "הופעת בלוז בפאב", "בלוז קלאסי עם מפוחית וגיטרה חשמלית", ["בלוז", "פאב", "גיטרה", "הופעה חיה"]),
        new(EventCategory.Nightlife, "מסיבת גג עם DJ", "סט דיפ האוס עד השעות הקטנות על גג עם נוף", ["מסיבה", "DJ", "גג", "ריקודים"]),
        new(EventCategory.Nightlife, "ערב טכנו במחסן", "מוזיקה אלקטרונית ותאורה במחסן תעשייתי", ["טכנו", "מוזיקה אלקטרונית", "מסיבה", "לילה"]),
        new(EventCategory.Nightlife, "קריוקי בפאב השכונתי", "שרים עם חברים, מבחר שירים בעברית ובאנגלית", ["קריוקי", "פאב", "שירה", "חברים"]),
        new(EventCategory.Nightlife, "מסיבת שנות ה־90", "הלהיטים הגדולים של שנות ה־90 וריקודים עד הבוקר", ["שנות ה־90", "נוסטלגיה", "ריקודים", "מסיבה"]),
        new(EventCategory.Nightlife, "ליל DJ על החוף", "מסיבה על החוף עם DJ ומדורה", ["חוף", "DJ", "מסיבה", "מדורה"]),
        new(EventCategory.Nightlife, "ערב סטנדאפ בבר", "שלושה קומיקאים צעירים מהצפון ומנחה", ["סטנדאפ", "קומדיה", "בר", "צחוק"]),
        new(EventCategory.Nightlife, "מסיבת סלסה ולטינו", "שיעור פתיחה למתחילים ואחריו ריקודים חופשיים", ["סלסה", "לטינו", "ריקודים", "שיעור"]),
        new(EventCategory.Nightlife, "ערב קוקטיילים וג׳אז", "קוקטיילים מיוחדים וטריו ג׳אז ברקע", ["קוקטיילים", "בר", "ג׳אז", "ערב"]),
        new(EventCategory.Food, "שוק איכרים גלילי", "תוצרת מקומית ישר מהחקלאים: ירקות, גבינות ודבש", ["שוק", "תוצרת מקומית", "ירקות", "משפחות"]),
        new(EventCategory.Food, "סדנת בישול גלילי", "מבשלים יחד מנות גליליות מסורתיות עם שף מקומי", ["בישול", "סדנה", "מטבח גלילי", "שף"]),
        new(EventCategory.Food, "פסטיבל יין בוטיק", "יקבי בוטיק מהגליל והגולן מציגים את היינות שלהם", ["יין", "יקב", "טעימות", "פסטיבל"]),
        new(EventCategory.Food, "ערב טעימות גבינות", "גבינות עיזים מהמחלבה ויין מקומי לצידן", ["גבינות", "טעימות", "מחלבה", "יין"]),
        new(EventCategory.Food, "ארוחת שף בכרם", "ארוחה בת חמש מנות בין שורות הגפנים", ["שף", "ארוחה", "כרם", "יין"]),
        new(EventCategory.Food, "פסטיבל אוכל רחוב", "דוכני אוכל רחוב, מוזיקה ופעילות לילדים", ["אוכל רחוב", "פסטיבל", "משפחות", "מוזיקה"]),
        new(EventCategory.Food, "סיור קולינרי בשוק", "סיור טעימות בין דוכני השוק עם מדריך", ["סיור", "קולינרי", "שוק", "טעימות"]),
        new(EventCategory.Food, "סדנת לחם מחמצת", "לומדים להכין מחמצת ואופים כיכר לקחת הביתה", ["לחם", "מחמצת", "אפייה", "סדנה"]),
        new(EventCategory.Workshops, "סדנת קרמיקה על האובניים", "עבודה על אובניים ויצירת כלי ראשון מחימר", ["קרמיקה", "אובניים", "יצירה", "סדנה"]),
        new(EventCategory.Workshops, "סדנת צילום בטבע", "צילום נוף ואור בשעת הזהב עם צלם טבע", ["צילום", "טבע", "מצלמה", "סדנה"]),
        new(EventCategory.Workshops, "סדנת קליגרפיה עברית", "כתיבה תמה בדיו ובקולמוס, מהאות הראשונה", ["קליגרפיה", "כתיבה", "יצירה", "אמנות"]),
        new(EventCategory.Workshops, "סדנת נגרות למתחילים", "בונים שרפרף מעץ מלא בעבודת יד", ["נגרות", "עץ", "יצירה", "סדנה"]),
        new(EventCategory.Workshops, "סדנת יוגה ונשימה", "תרגול יוגה עדין ותרגילי נשימה בטבע", ["יוגה", "נשימה", "בריאות", "רוגע"]),
        new(EventCategory.Workshops, "סדנת צמחי מרפא", "מזהים צמחי מרפא בשטח ומכינים תה ומשחה", ["צמחי מרפא", "טבע", "בריאות", "סדנה"]),
        new(EventCategory.Workshops, "סדנת ציור בצבעי מים", "ציור נוף הגליל בצבעי מים למתחילים", ["ציור", "צבעי מים", "אמנות", "יצירה"]),
        new(EventCategory.Workshops, "סדנת תכשיטים מכסף", "מעצבים ויוצקים טבעת או תליון מכסף", ["תכשיטים", "כסף", "יצירה", "סדנה"]),
        new(EventCategory.Outdoors, "טיול זריחה בהר מירון", "עלייה בחושך ותצפית זריחה מהפסגה", ["טיול", "זריחה", "הר", "טבע"]),
        new(EventCategory.Outdoors, "שייט קיאקים בירדן", "שייט רגוע בנהר הירדן, מתאים למשפחות", ["קיאקים", "ירדן", "מים", "משפחות"]),
        new(EventCategory.Outdoors, "רכיבת אופניים בעמק החולה", "רכיבה מישורית בין אגמון החולה והשדות", ["אופניים", "עמק החולה", "טבע", "רכיבה"]),
        new(EventCategory.Outdoors, "תצפית ציפורים באגמון", "עגורים ושקנאים בעונת הנדידה עם מדריך צפרות", ["ציפורים", "אגמון", "טבע", "נדידה"]),
        new(EventCategory.Outdoors, "טיול לילה לאור ירח", "הליכה שקטה בשביל ההר לאור הירח המלא", ["טיול לילה", "ירח", "טבע", "הליכה"]),
        new(EventCategory.Outdoors, "טיול משפחות לנחל עמוד", "מסלול מים קל עם בריכות טבעיות, מתאים לילדים", ["נחל", "מים", "משפחות", "טיול"]),
        new(EventCategory.Outdoors, "טיפוס סלעים למתחילים", "טיפוס מאובטח על סלע טבעי עם מדריכים", ["טיפוס", "סלעים", "אתגר", "טבע"]),
        new(EventCategory.Outdoors, "סיור כוכבים ואסטרונומיה", "תצפית בטלסקופים על כוכבי הלכת והירח", ["כוכבים", "אסטרונומיה", "לילה", "טבע"]),
        new(EventCategory.Culture, "תערוכת אמנות מקומית", "ציור, פיסול וצילום של אמנים מהגליל", ["תערוכה", "אמנות", "גלריה", "ציור"]),
        new(EventCategory.Culture, "הצגת תיאטרון רחוב", "הצגה קומית בחוצות העיר לכל המשפחה", ["תיאטרון", "רחוב", "משפחות", "הצגה"]),
        new(EventCategory.Culture, "סיור בעיר העתיקה", "סיור מודרך בסמטאות, בבתי הכנסת ובחאנים", ["סיור", "היסטוריה", "עיר עתיקה", "מדריך"]),
        new(EventCategory.Culture, "ערב שירה וספרות", "משוררים מהצפון קוראים משיריהם, עם מוזיקה חיה", ["שירה", "ספרות", "קריאה", "ערב"]),
        new(EventCategory.Culture, "הקרנת סרט באוויר הפתוח", "קולנוע תחת כיפת השמיים עם כריות ופופקורן", ["קולנוע", "סרט", "אוויר פתוח", "משפחות"]),
        new(EventCategory.Culture, "מופע מחול עכשווי", "להקת מחול צעירה במופע תנועה חדש", ["מחול", "מופע", "תנועה", "אמנות"]),
        new(EventCategory.Culture, "הרצאה על תולדות הגליל", "ארכיאולוג מספר על ממצאים חדשים מהגליל", ["הרצאה", "היסטוריה", "גליל", "ארכיאולוגיה"]),
        new(EventCategory.Culture, "פסטיבל סיפורי עם", "מספרי סיפורים, בובות ותיאטרון לילדים", ["סיפורים", "פסטיבל", "משפחות", "ילדים"]),
        new(EventCategory.Sports, "מרוץ 10 ק״מ בגליל", "מסלול ריצה בין כרמים ושדות, עם מקצה עממי", ["ריצה", "מרוץ", "ספורט", "קהילה"]),
        new(EventCategory.Sports, "טורניר כדורעף חופים", "טורניר זוגות פתוח עם מוזיקה על החוף", ["כדורעף", "חוף", "טורניר", "ספורט"]),
        new(EventCategory.Sports, "אימון קרוספיט בפארק", "אימון פתוח לכל הרמות עם מאמן מוסמך", ["קרוספיט", "אימון", "כושר", "פארק"]),
        new(EventCategory.Sports, "טורניר שחמט פתוח", "טורניר שחמט מהיר לכל הגילים", ["שחמט", "טורניר", "חשיבה", "קהילה"]),
        new(EventCategory.Sports, "יום גלישה בכנרת", "גלישת גלשני סאפ ורוח עם מדריכים", ["גלישה", "כנרת", "מים", "ספורט"]),
        new(EventCategory.Sports, "רכיבת שטח באופני הרים", "מסלול סינגל מאתגר ביער", ["אופני הרים", "רכיבה", "שטח", "אתגר"]),
        new(EventCategory.Sports, "שיעור פילאטיס בחוף", "פילאטיס על המזרן מול המים", ["פילאטיס", "חוף", "כושר", "בריאות"]),
        new(EventCategory.Sports, "משחק כדורגל קהילתי", "משחק ידידות פתוח לכל השכונה", ["כדורגל", "קהילה", "משחק", "ספורט"]),
        new(EventCategory.Other, "יריד יד שנייה", "בגדים, ספרים וחפצים במחירים סמליים", ["יריד", "יד שנייה", "קהילה", "קניות"]),
        new(EventCategory.Other, "ערב משחקי קופסה", "עשרות משחקים על השולחן ומנחה שמסביר חוקים", ["משחקי קופסה", "חברים", "ערב", "קהילה"]),
        new(EventCategory.Other, "יום פתוח בחווה", "מאכילים בעלי חיים, רכיבה על סוסים וקטיף", ["חווה", "בעלי חיים", "משפחות", "ילדים"]),
        new(EventCategory.Other, "שוק אמנים ומעצבים", "עבודות יד של אמנים ומעצבים מהאזור", ["אמנים", "עיצוב", "שוק", "קניות"]),
        new(EventCategory.Other, "ערב התנדבות בקהילה", "אורזים סלי מזון יחד לנזקקים בעיר", ["התנדבות", "קהילה", "ערב", "נתינה"]),
        new(EventCategory.Other, "מפגש טכנולוגיה ויזמות", "יזמים מהצפון מציגים מיזמים ומנטורים עונים", ["טכנולוגיה", "יזמות", "הרצאה", "מפגש"]),
        new(EventCategory.Other, "יריד ספרים", "הוצאות קטנות, סופרים מקומיים וספרים משומשים", ["ספרים", "קריאה", "יריד", "ספרות"]),
        new(EventCategory.Other, "ערב קוויז בפאב", "קוויז טריוויה בקבוצות עם פרסים", ["קוויז", "פאב", "חברים", "טריוויה"]),
    ];

    /// <summary>The catalogue covers today and the following 13 days.</summary>
    public const int ScheduleDays = 14;

    /// <summary>
    /// Where, when and at what price each catalogue event takes place, as if the catalogue were seeded at
    /// <paramref name="now"/>. The fixed random seed gives every call the same venues, prices, start hours
    /// and day offsets, so only the dates follow <paramref name="now"/>: "--seed-demo" creates the events
    /// from it and "--refresh-demo" moves them back onto it. The first event of each category is today, so
    /// the "today" feed has something in every category, and an event that would already be over moves to
    /// the next day.
    /// </summary>
    public static IReadOnlyList<DemoEventPlan> Schedule(DateTimeOffset now)
    {
        var random = new Random(2026);
        var today = JerusalemDays.Of(now);
        var firstOfCategory = new HashSet<EventCategory>();
        var highlighted = new HashSet<EventCategory> { EventCategory.Music, EventCategory.Nightlife, EventCategory.Food, EventCategory.Workshops, EventCategory.Outdoors, EventCategory.Culture };
        var plan = new List<DemoEventPlan>(Events.Length);
        foreach (var spec in Events)
        {
            var locality = Localities[random.Next(Localities.Length)];
            var hours = StartHours(spec.Category);
            var dayOffset = firstOfCategory.Add(spec.Category) ? 0 : random.Next(0, ScheduleDays);
            var start = JerusalemDays.StartUtc(today.AddDays(dayOffset)).AddHours(hours[random.Next(hours.Length)]).AddMinutes(random.Next(0, 2) * 30);
            var duration = TimeSpan.FromHours(DurationHours(spec.Category, random));
            if (start + duration <= now) start = start.AddDays(1);
            var price = Price(spec.Category, random);
            var venue = Venue(spec.Category, random);
            var latitude = locality.Latitude + (decimal)((random.NextDouble() - 0.5) * 0.01);
            var longitude = locality.Longitude + (decimal)((random.NextDouble() - 0.5) * 0.01);
            plan.Add(new DemoEventPlan(spec, locality, venue, price, latitude, longitude, start, start + duration, highlighted.Remove(spec.Category)));
        }

        return plan;
    }

    /// <summary>Hours of the day (Israel time) when events of a category usually start.</summary>
    public static int[] StartHours(EventCategory category) => category switch
    {
        EventCategory.Music => [19, 20, 21],
        EventCategory.Nightlife => [21, 22, 23],
        EventCategory.Food => [11, 12, 18, 19],
        EventCategory.Workshops => [10, 16, 17],
        EventCategory.Outdoors => [5, 7, 16],
        EventCategory.Culture => [17, 19, 20],
        EventCategory.Sports => [7, 8, 17],
        _ => [10, 18, 20],
    };

    public static int DurationHours(EventCategory category, Random random) => category switch
    {
        EventCategory.Nightlife => random.Next(4, 7),
        EventCategory.Outdoors => random.Next(3, 6),
        _ => random.Next(2, 4),
    };

    public static decimal Price(EventCategory category, Random random)
    {
        if (random.NextDouble() < 0.3) return 0;
        var (low, high) = category switch
        {
            EventCategory.Food or EventCategory.Workshops => (60, 180),
            EventCategory.Music or EventCategory.Nightlife => (30, 120),
            EventCategory.Outdoors => (20, 90),
            EventCategory.Culture => (20, 70),
            EventCategory.Sports => (0, 60),
            _ => (0, 30),
        };
        return random.Next(low, high + 1) / 5 * 5;
    }

    public static string Venue(EventCategory category, Random random) => Venues[category][random.Next(Venues[category].Length)];

    public static string Description(DemoEventSpec spec, string venue, DemoLocality locality, decimal price)
    {
        var cost = price == 0 ? "הכניסה חופשית." : $"מחיר כרטיס {price:0} ש״ח.";
        return $"{spec.Detail}. האירוע מתקיים ב{venue} ב{locality.Name}. {cost} מומלץ להגיע כמה דקות לפני תחילת האירוע.";
    }
}
