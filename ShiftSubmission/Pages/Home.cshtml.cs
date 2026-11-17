using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftSubmission.Pages;

public class HomeModel : PageModel
{
    public string MemberName { get; set; } = "";

    public IActionResult OnGet()
    {
        string? memberId =
            HttpContext.Session.GetString("MemberId");

        if (string.IsNullOrEmpty(memberId))
        {
            return RedirectToPage("/Login");
        }

        MemberName =
            HttpContext.Session.GetString("MemberName") ?? "スタッフ";

        return Page();
    }
}
