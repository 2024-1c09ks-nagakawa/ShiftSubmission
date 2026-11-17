
using System.Data;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ShiftSubmission.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration _configuration;

    public IndexModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // MemberAvailabilityから曜日ごとの勤務時間を取得

    // MemberAvailabilityから曜日ごとの勤務時間・休み設定を取得
    private void LoadAvailabilityDefaults()
    {
        long? memberId = CurrentMemberId;

        if (memberId == null)
        {
            return;
        }

        var settings =
            new Dictionary<int, (string Start, string End, bool Unavailable)>();

        using (var connection = new SqlConnection(ConnectionString))
        {
            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = @"
                SELECT
                    day_of_week,
                    available_start_time,
                    available_end_time,
                    is_unavailable
                FROM dbo.MemberAvailability
                WHERE M_id = @M_id;";

            command.Parameters.Add("@M_id", SqlDbType.Int).Value =
                checked((int)memberId.Value);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                int dayOfWeek =
                    Convert.ToInt32(reader["day_of_week"]);

                bool unavailable =
                    Convert.ToBoolean(reader["is_unavailable"]);

                string start = "09:00";
                string end = "17:00";

                if (!unavailable &&
                    reader["available_start_time"] != DBNull.Value)
                {
                    TimeSpan time =
                        (TimeSpan)reader["available_start_time"];

                    start =
                        $"{(int)time.TotalHours:00}:{time.Minutes:00}";
                }

                if (!unavailable &&
                    reader["available_end_time"] != DBNull.Value)
                {
                    TimeSpan time =
                        (TimeSpan)reader["available_end_time"];

                    end =
                        $"{(int)time.TotalHours:00}:{time.Minutes:00}";
                }

                settings[dayOfWeek] = (start, end, unavailable);
            }
        }

        // 各日付に曜日設定を反映
        foreach (var shift in Shifts)
        {
            int dayOfWeek = (int)shift.WorkDate.DayOfWeek;

            if (settings.TryGetValue(dayOfWeek, out var setting))
            {
                if (setting.Unavailable)
                {
                    shift.ShiftType = "休み";
                    shift.StartTime = "09:00";
                    shift.EndTime = "17:00";
                }
                else
                {
                    shift.ShiftType = "勤務";
                    shift.StartTime = setting.Start;
                    shift.EndTime = setting.End;
                }
            }
            else
            {
                // 曜日設定が登録されていない場合
                shift.ShiftType = "";
                shift.StartTime = "09:00";
                shift.EndTime = "17:00";
            }
        }
    }


    // ログイン中のスタッフID
    private long? CurrentMemberId
    {
        get
        {
            string? memberId = HttpContext.Session.GetString("MemberId");

            return long.TryParse(memberId, out long id)
                ? id
                : null;
        }
    }

    // ログイン確認
    private IActionResult? CheckLogin()
    {
        return CurrentMemberId == null
            ? RedirectToPage("/Login")
            : null;
    }

    // 接続文字列
    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "DefaultConnection が設定されていません。");

    // 提出対象年月
    [BindProperty]
    public DateTime TargetMonth { get; set; } =
        new(DateTime.Today.Year, DateTime.Today.Month, 1);

    // 各日のシフト希望
    [BindProperty]
    public List<ShiftRow> Shifts { get; set; } = new();

    public string? Message { get; set; }

    public bool IsSubmitted { get; set; }

    public class ShiftRow
    {
        public DateTime WorkDate { get; set; }

        public string ShiftType { get; set; } = "";

        public string StartTime { get; set; } = "09:00";

        public string EndTime { get; set; } = "17:00";
    }

    // 画面を開いたとき
    // 画面を開いたとき
    public IActionResult OnGet(int? year, int? month)
    {
        // ログイン確認
        var loginResult = CheckLogin();

        if (loginResult != null)
        {
            return loginResult;
        }

        // 対象月を設定
        if (year.HasValue && month.HasValue &&
            year.Value >= 2000 && year.Value <= 2100 &&
            month.Value >= 1 && month.Value <= 12)
        {
            TargetMonth = new DateTime(year.Value, month.Value, 1);
        }
        else
        {
            TargetMonth = new DateTime(
                DateTime.Today.Year,
                DateTime.Today.Month,
                1);
        }

        // 対象月の日付を作成
        CreateDates();

        // 曜日設定を自動反映
        LoadAvailabilityDefaults();

        // 提出済みか確認
        CheckSubmitted();

        // 提出済みなら保存済みデータで上書き
        if (IsSubmitted)
        {
            LoadSubmittedShifts();
        }

        return Page();
    }

    // シフト希望を提出したとき
    public IActionResult OnPost()
    {
        var loginResult = CheckLogin();

        if (loginResult != null)
        {
            return loginResult;
        }

        long memberId = CurrentMemberId!.Value;

        // 対象年月の検証
        if (TargetMonth < new DateTime(2000, 1, 1) ||
            TargetMonth > new DateTime(2100, 12, 1))
        {
            ModelState.AddModelError(
                "", "対象月を確認してください。");

            return Page();
        }

        // 日付一覧の検証
        if (Shifts == null || Shifts.Count == 0)
        {
            ModelState.AddModelError(
                "", "シフト希望がありません。");

            CreateDates();
            return Page();
        }

        // 入力内容の検証
        foreach (var shift in Shifts)
        {
            if (shift.WorkDate.Year != TargetMonth.Year ||
                shift.WorkDate.Month != TargetMonth.Month)
            {
                ModelState.AddModelError(
                    "", "日付が正しくありません。");

                break;
            }

            if (shift.ShiftType == "勤務")
            {
                bool validStart =
                    TimeSpan.TryParse(
                        shift.StartTime, out var start);

                bool validEnd =
                    TimeSpan.TryParse(
                        shift.EndTime, out var end);

                if (!validStart ||
                    !validEnd ||
                    start < TimeSpan.Zero ||
                    start >= TimeSpan.FromDays(1) ||
                    end <= TimeSpan.Zero ||
                    end >= TimeSpan.FromDays(1) ||
                    start >= end ||
                    start.TotalMinutes % 30 != 0 ||
                    end.TotalMinutes % 30 != 0)
                {
                    ModelState.AddModelError(
                        "",
                        $"{shift.WorkDate:M/d} の勤務時間を確認してください。");
                }
            }
            else if (shift.ShiftType != "" &&
                     shift.ShiftType != "休み")
            {
                ModelState.AddModelError(
                    "", "勤務区分が正しくありません。");
            }
        }

        // 何も選択されていない場合
        if (!Shifts.Any(s => s.ShiftType != ""))
        {
            ModelState.AddModelError(
                "", "勤務または休みを1日以上選択してください。");
        }

        // 入力エラーがあれば、入力値を保持して再表示
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            using var connection =
                new SqlConnection(ConnectionString);

            connection.Open();

            using var transaction =
                connection.BeginTransaction();

            try
            {
                // 対象月が提出済みか確認
                using (var checkCommand =
                       connection.CreateCommand())
                {
                    checkCommand.Transaction = transaction;

                    checkCommand.CommandText = @"
                        SELECT COUNT(*)
                        FROM dbo.ShiftSubmissionStatus
                        WHERE M_id = @M_id
                          AND TargetYear = @TargetYear
                          AND TargetMonth = @TargetMonth;";

                    checkCommand.Parameters.Add(
                        "@M_id", SqlDbType.BigInt).Value =
                        memberId;

                    checkCommand.Parameters.Add(
                        "@TargetYear", SqlDbType.Int).Value =
                        TargetMonth.Year;

                    checkCommand.Parameters.Add(
                        "@TargetMonth", SqlDbType.Int).Value =
                        TargetMonth.Month;

                    int count = Convert.ToInt32(
                        checkCommand.ExecuteScalar());

                    if (count > 0)
                    {
                        transaction.Rollback();

                        IsSubmitted = true;
                        Message =
                            "この月のシフト希望は提出済みです。";

                        CreateDates();
                        LoadSubmittedShifts();

                        return Page();
                    }
                }

                // 選択された日を保存
                foreach (var shift in Shifts)
                {
                    if (shift.ShiftType == "")
                    {
                        continue;
                    }

                    using var command =
                        connection.CreateCommand();

                    command.Transaction = transaction;

                    command.CommandText = @"
                        INSERT INTO dbo.ShiftRequest
                        (
                            M_id,
                            [date],
                            ST_id,
                            start_time,
                            end_time,
                            is_absent_request,
                            status
                        )
                        VALUES
                        (
                            @M_id,
                            @date,
                            @ST_id,
                            @start_time,
                            @end_time,
                            @is_absent_request,
                            1
                        );";

                    command.Parameters.Add(
                        "@M_id", SqlDbType.BigInt).Value =
                        memberId;

                    command.Parameters.Add(
                        "@date", SqlDbType.Date).Value =
                        shift.WorkDate.Date;

                    command.Parameters.Add(
                        "@ST_id", SqlDbType.BigInt).Value =
                        shift.ShiftType == "休み" ? 4L : 1L;

                    command.Parameters.Add(
                        "@start_time", SqlDbType.Time).Value =
                        shift.ShiftType == "勤務"
                            ? TimeSpan.Parse(shift.StartTime)
                            : DBNull.Value;

                    command.Parameters.Add(
                        "@end_time", SqlDbType.Time).Value =
                        shift.ShiftType == "勤務"
                            ? TimeSpan.Parse(shift.EndTime)
                            : DBNull.Value;

                    command.Parameters.Add(
                        "@is_absent_request", SqlDbType.Bit).Value =
                        shift.ShiftType == "休み";

                    command.ExecuteNonQuery();
                }

                // 提出済み年月を記録
                using (var submitCommand =
                       connection.CreateCommand())
                {
                    submitCommand.Transaction = transaction;

                    submitCommand.CommandText = @"
                        INSERT INTO dbo.ShiftSubmissionStatus
                        (
                            M_id,
                            TargetYear,
                            TargetMonth
                        )
                        VALUES
                        (
                            @M_id,
                            @TargetYear,
                            @TargetMonth
                        );";

                    submitCommand.Parameters.Add(
                        "@M_id", SqlDbType.BigInt).Value =
                        memberId;

                    submitCommand.Parameters.Add(
                        "@TargetYear", SqlDbType.Int).Value =
                        TargetMonth.Year;

                    submitCommand.Parameters.Add(
                        "@TargetMonth", SqlDbType.Int).Value =
                        TargetMonth.Month;

                    submitCommand.ExecuteNonQuery();
                }

                transaction.Commit();

                IsSubmitted = true;
                Message =
                    "シフト希望を提出しました。この月は再提出できません。";

                CreateDates();
                LoadSubmittedShifts();

                return Page();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // トランザクション終了済みの場合
                }

                throw;
            }
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(
                "", "保存エラー：" + ex.Message);

            // 入力したシフトは消さずに再表示する
            return Page();
        }
    }

    // 提出済みか確認
    private void CheckSubmitted()
    {
        long? memberId = CurrentMemberId;

        if (memberId == null)
        {
            return;
        }

        try
        {
            using var connection =
                new SqlConnection(ConnectionString);

            connection.Open();

            using var command =
                connection.CreateCommand();

            command.CommandText = @"
                SELECT COUNT(*)
                FROM dbo.ShiftSubmissionStatus
                WHERE M_id = @M_id
                  AND TargetYear = @TargetYear
                  AND TargetMonth = @TargetMonth;";

            command.Parameters.Add(
                "@M_id", SqlDbType.BigInt).Value =
                memberId.Value;

            command.Parameters.Add(
                "@TargetYear", SqlDbType.Int).Value =
                TargetMonth.Year;

            command.Parameters.Add(
                "@TargetMonth", SqlDbType.Int).Value =
                TargetMonth.Month;

            IsSubmitted =
                Convert.ToInt32(
                    command.ExecuteScalar()) > 0;
        }
        catch
        {
            // 確認できない場合はエラーを表示
            ModelState.AddModelError(
                "",
                "提出状況を確認できません。データベースを確認してください。");
        }
    }

    // 提出済みシフトを読み込む
    private void LoadSubmittedShifts()
    {
        long? memberId = CurrentMemberId;

        if (memberId == null)
        {
            return;
        }

        using var connection =
            new SqlConnection(ConnectionString);

        connection.Open();

        DateTime startDate = TargetMonth.Date;
        DateTime endDate = startDate.AddMonths(1);

        using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT
                [date],
                start_time,
                end_time,
                is_absent_request
            FROM dbo.ShiftRequest
            WHERE M_id = @M_id
              AND [date] >= @StartDate
              AND [date] < @EndDate;";

        command.Parameters.Add(
            "@M_id", SqlDbType.BigInt).Value =
            memberId.Value;

        command.Parameters.Add(
            "@StartDate", SqlDbType.Date).Value =
            startDate;

        command.Parameters.Add(
            "@EndDate", SqlDbType.Date).Value =
            endDate;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            DateTime workDate =
                Convert.ToDateTime(reader["date"]);

            ShiftRow? shift = Shifts.FirstOrDefault(
                s => s.WorkDate.Date == workDate.Date);

            if (shift == null)
            {
                continue;
            }

            bool isAbsent =
                Convert.ToBoolean(
                    reader["is_absent_request"]);

            if (isAbsent)
            {
                shift.ShiftType = "休み";
                continue;
            }

            shift.ShiftType = "勤務";

            if (reader["start_time"] != DBNull.Value)
            {
                TimeSpan startTime =
                    (TimeSpan)reader["start_time"];

                shift.StartTime =
                    $"{(int)startTime.TotalHours:00}:{startTime.Minutes:00}";
            }

            if (reader["end_time"] != DBNull.Value)
            {
                TimeSpan endTime =
                    (TimeSpan)reader["end_time"];

                shift.EndTime =
                    $"{(int)endTime.TotalHours:00}:{endTime.Minutes:00}";
            }
        }
    }

    // 対象月の日付一覧を作成
    private void CreateDates()
    {
        var firstDay = new DateTime(
            TargetMonth.Year,
            TargetMonth.Month,
            1);

        int days = DateTime.DaysInMonth(
            TargetMonth.Year,
            TargetMonth.Month);

        Shifts = Enumerable.Range(0, days)
            .Select(i => new ShiftRow
            {
                WorkDate = firstDay.AddDays(i),
                ShiftType = "",
                StartTime = "09:00",
                EndTime = "17:00"
            })
            .ToList();
    }
}
