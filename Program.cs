using System.Text.Json;

string? token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Write("Токен бота: ");
    token = Console.ReadLine();
}

var journal = Journal.Load();
var today = new List<ISpravka>();      // справки текущей смены
var kassa = new Kassa(today);

while (true)
{
    Console.WriteLine("\n=== Поликлиника: выдача справок ===");
    Console.WriteLine("1 - Выдать справку");
    Console.WriteLine("2 - Журнал выданных справок");
    Console.WriteLine("3 - Чек за смену");
    Console.WriteLine("4 - Повторно отправить неотправленные");
    Console.WriteLine("0 - Выход");
    Console.Write("> ");

    string? cmd = Console.ReadLine();
    if (cmd == "1") IssueSpravka();
    else if (cmd == "2") ShowJournal();
    else if (cmd == "3") kassa.PrintCheck(today);
    else if (cmd == "4") ResendUnsent();
    else if (cmd == "0") break;
    else Console.WriteLine("Неверный пункт меню.");
}

void IssueSpravka()
{
    Console.WriteLine("Тип: 1 - Справка 083, 2 - Санитарная книжка, 3 - Справка 086");
    Console.Write("> ");
    string? type = Console.ReadLine();
    if (type is not ("1" or "2" or "3"))
    {
        Console.WriteLine("Неверный тип.");
        return;
    }

    string fio = Ask("ФИО");
    string username = Ask("Telegram username (без @)").TrimStart('@');
    string birth = Ask("Дата рождения (дд.мм.гггг)");
    string blood = BloodTypes.Choose();
    var patient = new Patient(fio, blood, username, birth);

    ISpravka spravka;
    if (type == "1")
        spravka = new Spravka083(patient);
    else if (type == "2")
        spravka = new SanitarnayaKnizhka(patient);
    else
        spravka = new Spravka086(patient)
        {
            Destination = Ask("Куда подаётся справка"),
            Conclusion = Ask("Заключение врача")
        };

    Console.WriteLine();
    spravka.DisplaySpravkaInfo();

    INotifier notifier = new TelegramNotifier(token!, username);
    bool sent = notifier.Send(patient.Name, spravka.GetText());

    today.Add(spravka);
    journal.Add(new JournalEntry(
        DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
        fio, username, spravka.Name, spravka.CalculatePrice(), spravka.GetText())
    { Sent = sent });
    Journal.Save(journal);
}

void ShowJournal()
{
    if (journal.Count == 0)
    {
        Console.WriteLine("Журнал пуст.");
        return;
    }

    foreach (var e in journal)
        Console.WriteLine($"{e.Date} | {e.Fio} | @{e.Username} | {e.Type} | {e.Price:F2} | {(e.Sent ? "отправлена" : "НЕ отправлена")}");

    Console.WriteLine($"Всего: {journal.Count}, сумма: {journal.Sum(e => e.Price):F2}");
}

void ResendUnsent()
{
    var pending = journal.Where(e => !e.Sent && !string.IsNullOrEmpty(e.Text)).ToList();
    if (pending.Count == 0)
    {
        Console.WriteLine("Неотправленных справок нет.");
        return;
    }

    foreach (var e in pending)
    {
        Console.WriteLine($"Повторная отправка: {e.Fio} (@{e.Username})");
        INotifier notifier = new TelegramNotifier(token!, e.Username);
        e.Sent = notifier.Send(e.Fio, e.Text);
    }
    Journal.Save(journal);
}

static string Ask(string label)
{
    while (true)
    {
        Console.Write($"{label}: ");
        string? s = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        Console.WriteLine("Поле не может быть пустым.");
    }
}

// Пациент
class Patient(string name, string bloodType, string username, string birthDate)
{
    public string Name { get; } = name;
    public string BloodType { get; } = bloodType;
    public string Username { get; } = username;
    public string BirthDate { get; } = birthDate;
}

// Справки 
interface ISpravka
{
    string Name { get; }
    decimal CalculatePrice();
    string GetText();
    void DisplaySpravkaInfo();
    string GetShortInfo();
}

abstract class SpravkaBase(Patient patient) : ISpravka
{
    protected Patient Patient { get; } = patient;
    protected static string Today => DateTime.Today.ToString("dd.MM.yyyy");

    public abstract string Name { get; }
    public abstract decimal CalculatePrice();
    public abstract string GetText();

    public void DisplaySpravkaInfo() => Console.WriteLine(GetText());
    public string GetShortInfo() => $"{Name}: {CalculatePrice():F2}";
}

class Spravka083(Patient patient) : SpravkaBase(patient)
{
    public override string Name => "Справка 083";
    public override decimal CalculatePrice() => 100;

    public override string GetText() =>
        $"{Name}\n" +
        $"Дата выдачи: {Today}\n" +
        $"ФИО: {Patient.Name}\n" +
        $"Дата рождения: {Patient.BirthDate}\n" +
        $"Цена: {CalculatePrice():F2}";
}

class SanitarnayaKnizhka(Patient patient) : SpravkaBase(patient)
{
    public override string Name => "Санитарная книжка";
    public override decimal CalculatePrice() => 750;

    public override string GetText() =>
        $"{Name}\n" +
        $"Дата выдачи: {Today}\n" +
        $"ФИО: {Patient.Name}\n" +
        $"Дата рождения: {Patient.BirthDate}\n" +
        $"Цена: {CalculatePrice():F2}";
}

class Spravka086(Patient patient) : SpravkaBase(patient)
{
    public override string Name => "Справка 086";
    public string Destination { get; init; } = "";
    public string Conclusion { get; init; } = "";
    public int ValidMonths { get; init; } = 12;

    public override decimal CalculatePrice() => 300;

    public override string GetText() =>
        $"{Name} (форма 086/У)\n" +
        $"Дата выдачи: {Today}\n" +
        $"Куда: {Destination}\n" +
        $"ФИО: {Patient.Name}\n" +
        $"Дата рождения: {Patient.BirthDate}\n" +
        $"Группа крови: {Patient.BloodType}\n" +
        $"Заключение: {Conclusion}\n" +
        $"Срок действия: {ValidMonths} мес.\n" +
        $"Цена: {CalculatePrice():F2}";
}

// ===== Касса =====
class Kassa(List<ISpravka> spravki)
{
    public decimal Total() => spravki.Sum(s => s.CalculatePrice());

    public void PrintCheck(List<ISpravka> list)
    {
        if (list.Count == 0)
        {
            Console.WriteLine("За смену справок пока нет.");
            return;
        }

        foreach (var s in list)
            Console.WriteLine(s.GetShortInfo());
        Console.WriteLine($"Итого: {Total():F2}");
    }
}

// Журнал 
record JournalEntry(string Date, string Fio, string Username, string Type, decimal Price, string Text)
{
    public bool Sent { get; set; }
}

static class Journal
{
    const string FileName = "journal.json";

    static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<JournalEntry> Load()
    {
        try
        {
            if (!File.Exists(FileName)) return new();
            return JsonSerializer.Deserialize<List<JournalEntry>>(File.ReadAllText(FileName)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Save(List<JournalEntry> list)
    {
        File.WriteAllText(FileName, JsonSerializer.Serialize(list, options));
    }
}

// Группы крови 
static class BloodTypes
{
    static readonly string[] list =
    [
        "I (O) Rh+  - I положительная",
        "I (O) Rh-  - I отрицательная",
        "II (A) Rh+ - II положительная",
        "II (A) Rh- - II отрицательная (редкая)",
        "III (B) Rh+ - III положительная",
        "III (B) Rh- - III отрицательная",
        "IV (AB) Rh+ - IV положительная",
        "IV (AB) Rh- - IV отрицательная"
    ];

    public static string Choose()
    {
        Console.WriteLine("Группа крови:");
        for (int i = 0; i < list.Length; i++)
            Console.WriteLine($"  {i + 1} - {list[i]}");

        while (true)
        {
            Console.Write("Выберите номер (1-8): ");
            if (int.TryParse(Console.ReadLine(), out int n) && n >= 1 && n <= list.Length)
                return list[n - 1];

            Console.WriteLine("Неверный номер, попробуйте ещё раз.");
        }
    }
}

// Уведомления 
interface INotifier
{
    bool Send(string patientName, string message);
}

class TelegramNotifier(string token, string username) : INotifier
{
    static readonly HttpClient http = new();

    public bool Send(string patientName, string message)
    {
        try
        {
            long? chatId = FindChatId();
            if (chatId == null)
            {
                Console.WriteLine($"@{username} не найден. Пациент должен открыть бота и нажать Start.");
                return false;
            }

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = chatId.Value.ToString(),
                ["text"] = message
            });

            var resp = http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content).Result;
            if (resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"Справка отправлена пациенту: {patientName}");
                return true;
            }

            Console.WriteLine("Ошибка Telegram: " + resp.Content.ReadAsStringAsync().Result);
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Не удалось отправить: " + (ex.InnerException?.Message ?? ex.Message));
            return false;
        }
    }

    long? FindChatId()
    {
        string json = http.GetStringAsync($"https://api.telegram.org/bot{token}/getUpdates").Result;
        using var doc = JsonDocument.Parse(json);

        foreach (var update in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            if (!update.TryGetProperty("message", out var msg)) continue;
            if (!msg.TryGetProperty("from", out var from)) continue;
            if (!from.TryGetProperty("username", out var u)) continue;

            if (string.Equals(u.GetString(), username, StringComparison.OrdinalIgnoreCase))
                return msg.GetProperty("chat").GetProperty("id").GetInt64();
        }
        return null;
    }
}