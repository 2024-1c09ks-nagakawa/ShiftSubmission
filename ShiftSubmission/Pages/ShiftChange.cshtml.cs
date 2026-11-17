
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ShiftSubmission.Pages
{
    public class ShiftChangeModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public ShiftChangeModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "DefaultConnection が設定されていません。");

        private int? CurrentMemberId
        {
            get
            {
                string? value = HttpContext.Session.GetString("MemberId");

                return int.TryParse(value, out int id) ? id : null;
            }
        }

        [BindProperty]
        public DateTime TargetMonth { get; set; }
            = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        [BindProperty]
        public List<ShiftChangeRow> Shifts { get; set; } = new();

        public string Message { get; set; } = "";
        public string ErrorMessage { get; set; } = "";

        public class ShiftChangeRow
        {
            public DateTime WorkDate { get; set; }

            // 現在の確定シフト（dbo.Shift）
            public bool HasShift { get; set; }
            public string CurrentShift { get; set; } = "シフトなし";

            // スタッフが入力する変更希望
            public string? ShiftType { get; set; } = "";
            public string StartTime { get; set; } = "09:00";
            public string EndTime { get; set; } = "17:00";
        }

        public IActionResult OnGet(int? year, int? month)
        {
            if (CurrentMemberId == null)
            {
                return RedirectToPage("/Login");
            }

            if (year.HasValue && month.HasValue
                && year.Value >= 2000 && year.Value <= 2100
                && month.Value >= 1 && month.Value <= 12)
            {
                TargetMonth = new DateTime(year.Value, month.Value, 1);
            }

            LoadMonthShifts(CurrentMemberId.Value);
            return Page();
        }

        public IActionResult OnPost()
        {
            if (CurrentMemberId == null)
            {
                return RedirectToPage("/Login");
            }

            if (TargetMonth.Year < 2000 || TargetMonth.Year > 2100
                || TargetMonth.Month < 1 || TargetMonth.Month > 12)
            {
                ErrorMessage = "対象月が正しくありません。";
                LoadMonthShifts(CurrentMemberId.Value);
                return Page();
            }

            TargetMonth = new DateTime(
                TargetMonth.Year, TargetMonth.Month, 1);

            if (Shifts == null || Shifts.Count == 0)
            {
                ErrorMessage = "変更依頼の内容がありません。";
                LoadMonthShifts(CurrentMemberId.Value);
                return Page();
            }

            foreach (var shift in Shifts)
            {
                if (shift.WorkDate.Year != TargetMonth.Year
                    || shift.WorkDate.Month != TargetMonth.Month)
                {
                    ErrorMessage = "対象月以外の日付が含まれています。";
                    LoadMonthShifts(CurrentMemberId.Value);
                    return Page();
                }

                if (!string.IsNullOrEmpty(shift.ShiftType)
                    && shift.ShiftType != "勤務"
                    && shift.ShiftType != "休み")
                {
                    ErrorMessage = "シフトの選択内容が正しくありません。";
                    LoadMonthShifts(CurrentMemberId.Value);
                    return Page();
                }

                
            }

            var selectedShifts = Shifts
                .Where(s => s.ShiftType == "勤務" || s.ShiftType == "休み")
                .ToList();

            if (selectedShifts.Count == 0)
            {
                ErrorMessage = "変更する日を選択してください。";
                LoadMonthShifts(CurrentMemberId.Value);
                return Page();
            }

            if (selectedShifts.GroupBy(s => s.WorkDate.Date)
                .Any(g => g.Count() > 1))
            {
                ErrorMessage = "同じ日付が重複しています。";
                LoadMonthShifts(CurrentMemberId.Value);
                return Page();
            }

            foreach (var shift in selectedShifts)
            {
                if (shift.WorkDate.Date < DateTime.Today.AddDays(7))
                {
                    ErrorMessage =
                        $"{shift.WorkDate:M月d日} は申請期限を過ぎています。勤務日の7日前までに申請してください。";

                    LoadMonthShifts(CurrentMemberId.Value);
                    return Page();
                }

                if (shift.ShiftType == "勤務")
                {
                    if (!TimeSpan.TryParse(shift.StartTime, out var start)
                        || !TimeSpan.TryParse(shift.EndTime, out var end))
                    {
                        ErrorMessage = "勤務開始時刻と終了時刻を選択してください。";
                        LoadMonthShifts(CurrentMemberId.Value);
                        return Page();
                    }

                    if (start >= end)
                    {
                        ErrorMessage = "終了時刻は開始時刻より後にしてください。";
                        LoadMonthShifts(CurrentMemberId.Value);
                        return Page();
                    }

                    if (start.Seconds != 0 || end.Seconds != 0
                        || start.Minutes % 30 != 0
                        || end.Minutes % 30 != 0)
                    {
                        ErrorMessage = "時刻は30分単位で指定してください。";
                        LoadMonthShifts(CurrentMemberId.Value);
                        return Page();
                    }
                }
            }

            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var shift in selectedShifts)
                {
                    // 元の確定シフトIDと勤務時間を取得
                    int shiftId;
                    TimeSpan originalStart;
                    TimeSpan originalEnd;

                    using var checkCommand = new SqlCommand(@"
                        SELECT TOP (1)
                            S_id,
                            start_at,
                            end_at
                        FROM dbo.Shift
                        WHERE M_id = @M_id
                          AND [date] = @WorkDate
                        ORDER BY S_id;",
                        connection, transaction);

                    checkCommand.Parameters.Add(
                        "@M_id", SqlDbType.Int).Value =
                        CurrentMemberId.Value;

                    checkCommand.Parameters.Add(
                        "@WorkDate", SqlDbType.Date).Value =
                        shift.WorkDate.Date;

                    using (var reader = checkCommand.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            throw new InvalidOperationException(
                                $"{shift.WorkDate:M月d日} には確定シフトがありません。");
                        }

                        shiftId = reader.GetInt32(0);
                        originalStart = reader.GetTimeSpan(1);
                        originalEnd = reader.GetTimeSpan(2);
                    }

                    // 変更依頼を保存
                    int shiftTypeId =
                        shift.ShiftType == "休み" ? 4 : 1;

                    object startTime = DBNull.Value;
                    object endTime = DBNull.Value;

                    TimeSpan requestedStart = TimeSpan.Zero;
                    TimeSpan requestedEnd = TimeSpan.Zero;

                    if (shift.ShiftType == "勤務")
                    {
                        requestedStart = TimeSpan.Parse(shift.StartTime);
                        requestedEnd = TimeSpan.Parse(shift.EndTime);

                        startTime = requestedStart;
                        endTime = requestedEnd;
                    }

                    using var insertCommand = new SqlCommand(@"
                        INSERT INTO dbo.ShiftChangeRequest
                        (
                            M_id,
                            [date],
                            ST_id,
                            start_time,
                            end_time
                        )
                        VALUES
                        (
                            @M_id,
                            @WorkDate,
                            @ST_id,
                            @StartTime,
                            @EndTime
                        );",
                        connection, transaction);

                    insertCommand.Parameters.Add(
                        "@M_id", SqlDbType.Int).Value =
                        CurrentMemberId.Value;

                    insertCommand.Parameters.Add(
                        "@WorkDate", SqlDbType.Date).Value =
                        shift.WorkDate.Date;

                    insertCommand.Parameters.Add(
                        "@ST_id", SqlDbType.Int).Value =
                        shiftTypeId;

                    insertCommand.Parameters.Add(
                        "@StartTime", SqlDbType.Time).Value =
                        startTime;

                    insertCommand.Parameters.Add(
                        "@EndTime", SqlDbType.Time).Value =
                        endTime;

                    insertCommand.ExecuteNonQuery();

                    // 代打募集する時間帯を決定
                    var recruitmentTimes =
                        new List<(TimeSpan Start, TimeSpan End)>();

                    if (shift.ShiftType == "休み")
                    {
                        // 休みの場合は全時間を募集
                        recruitmentTimes.Add(
                            (originalStart, originalEnd));
                    }
                    else
                    {
                        // 出勤時間を遅らせた場合
                        if (requestedStart > originalStart)
                        {
                            recruitmentTimes.Add(
                                (originalStart, requestedStart));
                        }

                        // 退勤時間を早めた場合
                        if (requestedEnd < originalEnd)
                        {
                            recruitmentTimes.Add(
                                (requestedEnd, originalEnd));
                        }
                    }

                    // 代打募集を登録
                    foreach (var recruitmentTime in recruitmentTimes)
                    {
                        using var recruitmentCommand = new SqlCommand(@"
                            IF NOT EXISTS
                            (
                                SELECT 1
                                FROM dbo.ShiftSubstituteRecruitment
                                    WITH (UPDLOCK, HOLDLOCK)
                                WHERE S_id = @S_id
                                  AND RecruitmentStartAt = @StartAt
                                  AND RecruitmentEndAt = @EndAt
                                  AND Status = 1
                            )
                            BEGIN
                                INSERT INTO dbo.ShiftSubstituteRecruitment
                                (
                                    S_id,
                                    Requester_M_id,
                                    Status,
                                    RecruitmentStartAt,
                                    RecruitmentEndAt
                                )
                                VALUES
                                (
                                    @S_id,
                                    @M_id,
                                    1,
                                    @StartAt,
                                    @EndAt
                                );
                                END;",
                            connection, transaction);

                        recruitmentCommand.Parameters.Add(
                            "@S_id", SqlDbType.Int).Value = shiftId;

                        recruitmentCommand.Parameters.Add(
                            "@M_id", SqlDbType.Int).Value =
                            CurrentMemberId.Value;

                        recruitmentCommand.Parameters.Add(
                            "@StartAt", SqlDbType.Time).Value =
                            recruitmentTime.Start;

                        recruitmentCommand.Parameters.Add(
                            "@EndAt", SqlDbType.Time).Value =
                            recruitmentTime.End;

                        recruitmentCommand.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // ロールバックに失敗した場合は元のエラーを表示
                }

                ErrorMessage = ex is InvalidOperationException
                    ? ex.Message
                    : "保存に失敗しました。データベースの設定を確認してください。";

                LoadMonthShifts(CurrentMemberId.Value);
                return Page();
            }

            // 正常に保存できた場合
            transaction.Commit();

            Message = "シフト変更依頼と代打募集を保存しました。";

            LoadMonthShifts(CurrentMemberId.Value);
            return Page();
        }

        private void LoadMonthShifts(int memberId)
        {
            Shifts = new List<ShiftChangeRow>();

            int days = DateTime.DaysInMonth(
                TargetMonth.Year, TargetMonth.Month);

            for (int day = 1; day <= days; day++)
            {
                Shifts.Add(new ShiftChangeRow
                {
                    WorkDate = new DateTime(
                        TargetMonth.Year, TargetMonth.Month, day)
                });
            }

            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = @"
                SELECT [date], start_at, end_at, break_minutes, note
                FROM dbo.Shift
                WHERE M_id = @M_id
                  AND [date] >= @StartDate
                  AND [date] < @EndDate
                ORDER BY [date];";

            command.Parameters.Add(
                "@M_id", SqlDbType.Int).Value = memberId;

            command.Parameters.Add(
                "@StartDate", SqlDbType.Date).Value =
                TargetMonth.Date;

            command.Parameters.Add(
                "@EndDate", SqlDbType.Date).Value =
                TargetMonth.AddMonths(1);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                DateTime workDate = reader.GetDateTime(0).Date;
                int index = workDate.Day - 1;

                if (index < 0 || index >= Shifts.Count)
                {
                    continue;
                }

                TimeSpan start = reader.GetTimeSpan(1);
                TimeSpan end = reader.GetTimeSpan(2);
                int breakMinutes = reader.IsDBNull(3)
                    ? 0
                    : reader.GetInt32(3);

                string currentShift =
                    $"{start:hh\\:mm}～{end:hh\\:mm}";

                currentShift += $"（休憩 {breakMinutes}分）";

                if (!reader.IsDBNull(4))
                {
                    string note = reader.GetString(4);

                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        currentShift += $" 備考：{note}";
                    }
                }

                Shifts[index].HasShift = true;
                Shifts[index].CurrentShift = currentShift;
            }
        }
    }
}
