
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ShiftSubmission.Pages;

public class AvailabilityModel : PageModel
{
    private readonly IConfiguration _configuration;

    public AvailabilityModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private int? CurrentMemberId
    {
        get
        {
            string? id = HttpContext.Session.GetString("MemberId");
            return int.TryParse(id, out int memberId)
                ? memberId
                : null;
        }
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "DefaultConnection ‚ªİ’è‚³‚ê‚Ä‚¢‚Ü‚¹‚ñB");

    [BindProperty]
    public List<AvailabilityRow> Days { get; set; } = new();

    public string? Message { get; set; }

    public class AvailabilityRow
    {
        public int DayOfWeek { get; set; }
        public string DayName { get; set; } = "";
        public string StartTime { get; set; } = "09:00";
        public string EndTime { get; set; } = "17:00";
        public bool IsUnavailable { get; set; }
    }

    private static readonly string[] DayNames =
    {
        "“ú—j“ú", "Œ—j“ú", "‰Î—j“ú", "…—j“ú",
        "–Ø—j“ú", "‹à—j“ú", "“y—j“ú"
    };

    public IActionResult OnGet()
    {
        if (CurrentMemberId == null)
            return RedirectToPage("/Login");

        LoadDays();
        return Page();
    }

    public IActionResult OnPost()
    {
        if (CurrentMemberId == null)
            return RedirectToPage("/Login");

        if (Days == null || Days.Count != 7)
        {
            ModelState.AddModelError("", "—j“ú‚Ìİ’è‚ğŠm”F‚µ‚Ä‚­‚¾‚³‚¢B");
            LoadDays();
            return Page();
        }

        var seenDays = new HashSet<int>();

        foreach (var day in Days)
        {
            if (day.DayOfWeek < 0 || day.DayOfWeek > 6 ||
                !seenDays.Add(day.DayOfWeek))
            {
                ModelState.AddModelError("", "—j“ú‚Ìw’è‚ª³‚µ‚­‚ ‚è‚Ü‚¹‚ñB");
                LoadDays();
                return Page();
            }

            if (day.IsUnavailable)
                continue;

            if (!TimeSpan.TryParse(day.StartTime, out var start) ||
                !TimeSpan.TryParse(day.EndTime, out var end) ||
                start < TimeSpan.Zero ||
                start >= TimeSpan.FromDays(1) ||
                end <= TimeSpan.Zero ||
                end >= TimeSpan.FromDays(1) ||
                start >= end ||
                start.TotalMinutes % 30 != 0 ||
                end.TotalMinutes % 30 != 0)
            {
                ModelState.AddModelError(
                    "", $"{DayNames[day.DayOfWeek]}‚ÌŠÔ‚ğŠm”F‚µ‚Ä‚­‚¾‚³‚¢B");
            }
        }

        if (!ModelState.IsValid)
            return Page();

        using var connection = new SqlConnection(ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var day in Days)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;

                command.CommandText = @"
IF EXISTS (
    SELECT 1
    FROM dbo.MemberAvailability
    WHERE M_id = @M_id AND day_of_week = @day
)
BEGIN
    UPDATE dbo.MemberAvailability
    SET available_start_time = @start,
        available_end_time = @end,
        is_unavailable = @unavailable
    WHERE M_id = @M_id AND day_of_week = @day;
END
ELSE
BEGIN
    INSERT INTO dbo.MemberAvailability
        (M_id, day_of_week, available_start_time,
         available_end_time, is_unavailable)
    VALUES
        (@M_id, @day, @start, @end, @unavailable);
END";

                command.Parameters.Add("@M_id", SqlDbType.Int).Value =
                    CurrentMemberId.Value;

                command.Parameters.Add("@day", SqlDbType.TinyInt).Value =
                    day.DayOfWeek;

                command.Parameters.Add("@start", SqlDbType.Time).Value =
                    day.IsUnavailable
                        ? DBNull.Value
                        : TimeSpan.Parse(day.StartTime);

                command.Parameters.Add("@end", SqlDbType.Time).Value =
                    day.IsUnavailable
                        ? DBNull.Value
                        : TimeSpan.Parse(day.EndTime);

                command.Parameters.Add("@unavailable", SqlDbType.Bit).Value =
                    day.IsUnavailable;

                command.ExecuteNonQuery();
            }

            transaction.Commit();
            Message = "—j“ú•Ê‚Ìo‹Î‰Â”\ŠÔ‚ğ•Û‘¶‚µ‚Ü‚µ‚½B";
            LoadDays();
            return Page();
        }
        catch (Exception ex)
        {
            try { transaction.Rollback(); }
            catch { }

            ModelState.AddModelError("", "•Û‘¶ƒGƒ‰[F" + ex.Message);
            return Page();
        }
    }

    private void LoadDays()
    {
        Days = new List<AvailabilityRow>();

        for (int i = 0; i < 7; i++)
        {
            Days.Add(new AvailabilityRow
            {
                DayOfWeek = i,
                DayName = DayNames[i],
                StartTime = "09:00",
                EndTime = "17:00"
            });
        }

        using var connection = new SqlConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT day_of_week, available_start_time,
       available_end_time, is_unavailable
FROM dbo.MemberAvailability
WHERE M_id = @M_id;";

        command.Parameters.Add("@M_id", SqlDbType.Int).Value =
            CurrentMemberId!.Value;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            int dayNumber = Convert.ToInt32(reader["day_of_week"]);
            var day = Days.Find(d => d.DayOfWeek == dayNumber);

            if (day == null)
                continue;

            day.IsUnavailable = Convert.ToBoolean(reader["is_unavailable"]);

            if (reader["available_start_time"] != DBNull.Value)
            {
                var start = (TimeSpan)reader["available_start_time"];
                day.StartTime = $"{(int)start.TotalHours:00}:{start.Minutes:00}";
            }

            if (reader["available_end_time"] != DBNull.Value)
            {
                var end = (TimeSpan)reader["available_end_time"];
                day.EndTime = $"{(int)end.TotalHours:00}:{end.Minutes:00}";
            }
        }
    }
}
