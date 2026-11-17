
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ShiftSubmission.Pages
{
    public class AnnouncementsModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public AnnouncementsModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public List<AnnouncementItem> Announcements { get; set; } = new();

        public void OnGet()
        {
            string connectionString =
                _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "DefaultConnection Ç™ê›íËÇ≥ÇÍÇƒÇ¢Ç‹ÇπÇÒÅB");

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            string sql = @"
                SELECT announce_id, title, header, content, posted_date
                FROM dbo.Announce
                ORDER BY posted_date DESC, announce_id DESC;";

            using var command = new SqlCommand(sql, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                Announcements.Add(new AnnouncementItem
                {
                    AnnounceId = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Header = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Content = reader.GetString(3),
                    PostedDate = reader.GetDateTime(4)
                });
            }
        }

        public class AnnouncementItem
        {
            public int AnnounceId { get; set; }
            public string Title { get; set; } = "";
            public string Header { get; set; } = "";
            public string Content { get; set; } = "";
            public DateTime PostedDate { get; set; }
        }
    }
}
