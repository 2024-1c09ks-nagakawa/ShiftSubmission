using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ShiftSubmission.Pages;

public class ShiftConfirmModel : PageModel
{
    private readonly IConfiguration _configuration;

    public ShiftConfirmModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime SelectedDate { get; set; }
    public string ShiftTime { get; set; } = "シフトなし";
    public List<CalendarDay> CalendarDays { get; set; } = new();

    public class CalendarDay
    {
        public DateTime? Date { get; set; }
        public bool IsSelected { get; set; }
        public bool HasShift { get; set; }
    }

    public IActionResult OnGet(
        int? year,
        int? month,
        int? day,
        string? targetMonth)
    {
        string? memberIdText = HttpContext.Session.GetString("MemberId");

        if (!long.TryParse(memberIdText, out long memberId))
        {
            return RedirectToPage("/Login");
        }

        DateTime today = DateTime.Today;

        if (!string.IsNullOrWhiteSpace(targetMonth) &&
            DateTime.TryParseExact(
                targetMonth,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime selectedMonth))
        {
            Year = selectedMonth.Year;
            Month = selectedMonth.Month;
        }
        else
        {
            Year = year ?? today.Year;
            Month = month ?? today.Month;
        }

        if (Year < 1 || Year > 9998 || Month < 1 || Month > 12)
        {
            return RedirectToPage("/ShiftConfirm");
        }

        int lastDay = DateTime.DaysInMonth(Year, Month);
        int selectedDay = Math.Clamp(
            day ?? Math.Min(today.Day, lastDay), 1, lastDay);

        SelectedDate = new DateTime(Year, Month, selectedDay);

        LoadCalendar(memberId);
        LoadShift(memberId);

        return Page();
    }

    private SqlConnection CreateConnection()
    {
        string connectionString =
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "DefaultConnection が設定されていません。");

        return new SqlConnection(connectionString);
    }

    private void LoadCalendar(long memberId)
    {
        int daysInMonth = DateTime.DaysInMonth(Year, Month);
        DateTime firstDay = new(Year, Month, 1);
        int leadingDays = (int)firstDay.DayOfWeek;

        for (int i = 0; i < leadingDays; i++)
        {
            CalendarDays.Add(new CalendarDay());
        }

        var shiftDates = new HashSet<DateTime>();

        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
        SELECT [date]
        FROM dbo.Shift
        WHERE M_id = @MemberId
          AND [date] >= @StartDate
          AND [date] < @EndDate;";

        command.Parameters.Add("@MemberId", SqlDbType.BigInt).Value = memberId;
        command.Parameters.Add("@StartDate", SqlDbType.Date).Value =
            new DateTime(Year, Month, 1);
        command.Parameters.Add("@EndDate", SqlDbType.Date).Value =
            new DateTime(Year, Month, 1).AddMonths(1);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            shiftDates.Add(reader.GetDateTime(0).Date);
        }

        for (int day = 1; day <= daysInMonth; day++)
        {
            DateTime date = new(Year, Month, day);

            CalendarDays.Add(new CalendarDay
            {
                Date = date,
                IsSelected = date == SelectedDate,
                HasShift = shiftDates.Contains(date)
            });
        }

        while (CalendarDays.Count % 7 != 0)
        {
            CalendarDays.Add(new CalendarDay());
        }
    }

    private void LoadShift(long memberId)
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = @"
        SELECT start_at, end_at, break_minutes, note
        FROM dbo.Shift
        WHERE M_id = @MemberId
          AND [date] = @WorkDate;";

        command.Parameters.Add("@MemberId", SqlDbType.BigInt).Value =
            memberId;

        command.Parameters.Add("@WorkDate", SqlDbType.Date).Value =
            SelectedDate.Date;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            ShiftTime = "シフトなし";
            return;
        }

        TimeSpan start = reader.GetTimeSpan(0);
        TimeSpan end = reader.GetTimeSpan(1);
        int breakMinutes = reader.GetInt32(2);

        ShiftTime = $"{start:hh\\:mm}～{end:hh\\:mm}"
            + $"（休憩 {breakMinutes}分）";

        if (!reader.IsDBNull(3))
        {
            string note = reader.GetString(3);

            if (!string.IsNullOrWhiteSpace(note))
            {
                ShiftTime += $"　備考：{note}";
            }
        }
    }
}