
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace ShiftSubmission.Pages;

public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [BindProperty]
    public string LoginId { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        Console.WriteLine("ログイン処理を開始しました");


        if (!long.TryParse(LoginId, out long loginId) ||
            string.IsNullOrWhiteSpace(Password))
        {
            ModelState.AddModelError(
                "", "ログインIDまたはパスワードが違います。");
            return Page();
        }

        string connectionString =
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "DefaultConnection が設定されていません。");

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        Console.WriteLine("データベース接続成功");

        using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT M_id, M_name
            FROM dbo.Member
            WHERE login_id = @LoginId
              AND password_hash = @Password;";

        command.Parameters.Add(
            "@LoginId", SqlDbType.BigInt).Value = loginId;

        command.Parameters.Add(
            "@Password", SqlDbType.NVarChar, 255).Value = Password;

        using var reader = command.ExecuteReader();
        Console.WriteLine("SQL実行成功");

        if (!reader.Read())
        {
            Console.WriteLine("ログインIDまたはパスワードが一致しません");
            ModelState.AddModelError(
                "", "ログインIDまたはパスワードが違います。");
            return Page();
        }
        Console.WriteLine("ログイン成功");

        object idValue = reader.GetValue(0);

        Console.WriteLine("M_idの型: " + idValue.GetType().FullName);
        Console.WriteLine("M_idの値: " + idValue);

        long memberId = Convert.ToInt64(idValue);
        string memberName = reader.GetString(1);

        reader.Close();

        HttpContext.Session.SetString(
            "MemberId",
            memberId.ToString()
        );

        HttpContext.Session.SetString(
            "MemberName",
            memberName
        );

        return RedirectToPage("/Home");
    }
}
