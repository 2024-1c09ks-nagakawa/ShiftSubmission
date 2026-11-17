using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ShiftSubmission.Pages
{
    public class SubstituteModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public SubstituteModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public int? CurrentMemberId { get; set; }

        public string MemberName { get; set; } = "";

        public List<RecruitmentInfo> RecruitmentList { get; set; } = new();

        public string Message { get; set; } = "";

        [BindProperty]
        public int RecruitmentId { get; set; }

        public class RecruitmentInfo
        {
            public int RecruitmentId { get; set; }
            public DateTime WorkDate { get; set; }
            public TimeSpan StartAt { get; set; }
            public TimeSpan EndAt { get; set; }
            public string RequesterName { get; set; } = "";
        }

        public IActionResult OnGet()
        {
            string? memberIdString =
                HttpContext.Session.GetString("MemberId");

            if (!int.TryParse(memberIdString, out int memberId))
            {
                return RedirectToPage("/Login");
            }

            CurrentMemberId = memberId;

            MemberName = HttpContext.Session.GetString("MemberName") ?? "";

            LoadRecruitments();

            return Page();
        }


        public IActionResult OnPostApply()
        {
            // ログイン中のスタッフIDを取得
            string? memberIdString =
                HttpContext.Session.GetString("MemberId");

            if (!int.TryParse(memberIdString, out int memberId))
            {
                return RedirectToPage("/Login");
            }

            CurrentMemberId = memberId;

            MemberName = HttpContext.Session.GetString("MemberName") ?? "";

            string connectionString =
                _configuration.GetConnectionString("DefaultConnection")!;

            using SqlConnection connection = new SqlConnection(connectionString);
            connection.Open();

            using SqlTransaction transaction = connection.BeginTransaction();

            try
            {
                // 募集情報をロックして取得
                string checkSql = @"
            SELECT Requester_M_id
            FROM dbo.ShiftSubstituteRecruitment
                WITH (UPDLOCK, HOLDLOCK)
            WHERE RecruitmentId = @RecruitmentId
              AND Status = 1;
        ";

                int requesterId;

                using (SqlCommand command = new SqlCommand(
                    checkSql, connection, transaction))
                {
                    command.Parameters.AddWithValue(
                        "@RecruitmentId", RecruitmentId);

                    object? result = command.ExecuteScalar();

                    if (result == null)
                    {
                        transaction.Rollback();

                        Message = "この募集は既に応募できません。";
                        LoadRecruitments();
                        return Page();
                    }

                    requesterId = Convert.ToInt32(result);
                }

                // 募集者本人の応募を禁止
                if (requesterId == memberId)
                {
                    transaction.Rollback();

                    Message = "自分が出した募集には応募できません。";
                    LoadRecruitments();
                    return Page();
                }

                // 応募情報を登録
                string insertSql = @"
            INSERT INTO dbo.ShiftSubstituteApplication
            (
                RecruitmentId,
                M_id
            )
            VALUES
            (
                @RecruitmentId,
                @MemberId
            );
        ";

                using (SqlCommand command = new SqlCommand(
                    insertSql, connection, transaction))
                {
                    command.Parameters.AddWithValue(
                        "@RecruitmentId", RecruitmentId);

                    command.Parameters.AddWithValue(
                        "@MemberId", memberId);

                    command.ExecuteNonQuery();
                }

                // 最初の応募者を仮決定
                string updateSql = @"
            UPDATE dbo.ShiftSubstituteRecruitment
            SET
                Status = 4,
                SelectedMemberId = @MemberId,
                SelectedAt = SYSDATETIME()
            WHERE RecruitmentId = @RecruitmentId
              AND Status = 1;
        ";

                using (SqlCommand command = new SqlCommand(
                    updateSql, connection, transaction))
                {
                    command.Parameters.AddWithValue(
                        "@MemberId", memberId);

                    command.Parameters.AddWithValue(
                        "@RecruitmentId", RecruitmentId);

                    command.ExecuteNonQuery();
                }

                transaction.Commit();

                Message = "応募しました。管理者の承認をお待ちください。";
            }
            catch (SqlException ex)
            {
                transaction.Rollback();

                Console.WriteLine("代打応募SQLエラー:");
                Console.WriteLine(ex.ToString());

                Message = "応募に失敗しました。エラー内容：" + ex.Message;
            }

            LoadRecruitments();

            return Page();
        }



        private void LoadRecruitments()
        {
            string connectionString =
                _configuration.GetConnectionString("DefaultConnection")!;

            using SqlConnection connection = new SqlConnection(connectionString);
            connection.Open();

            string sql = @"
                SELECT
                    r.RecruitmentId,
                    s.[date] AS WorkDate,
                    r.RecruitmentStartAt,
                    r.RecruitmentEndAt,
                    m.M_name AS RequesterName
                FROM dbo.ShiftSubstituteRecruitment r
                INNER JOIN dbo.Shift s
                    ON r.S_id = s.S_id
                INNER JOIN dbo.Member m
                ON r.Requester_M_id = m.M_id
                WHERE r.Status = 1
                ORDER BY s.[date], r.RecruitmentStartAt;
            ";

            using SqlCommand command = new SqlCommand(sql, connection);
            using SqlDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                RecruitmentList.Add(new RecruitmentInfo
                {
                    RecruitmentId = reader.GetInt32(0),
                    WorkDate = reader.GetDateTime(1),
                    StartAt = reader.GetTimeSpan(2),
                    EndAt = reader.GetTimeSpan(3),
                    RequesterName = reader.GetString(4)
                });
            }
        }
    }
}