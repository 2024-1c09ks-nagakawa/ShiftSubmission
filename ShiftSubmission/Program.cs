
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// 1. Koyebが指定するPORT環境変数（デフォルト8000）で待ち受け
var port = Environment.GetEnvironmentVariable("PORT") ?? "8000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Razor Pages
builder.Services.AddRazorPages();

// セッションに必要なサービス
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// 2. Koyeb（リバースプロキシ）経由のHTTPS通信を正しく認識させる設定
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// HTTPリクエストの設定
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// ※Koyeb側でHTTPS終端されるため、ローカル開発時のみHTTPSリダイレクトを有効化すると安全です
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseRouting();

// セッションを有効化（Authorizationより前）
app.UseSession();

app.UseAuthorization();

// 既存のRazor Pagesルーティング
app.MapRazorPages();

// 3. スリープ防止・死活監視用エンドポイント
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// 4. LINE Webhook受信用APIエンドポイント
app.MapPost("/api/line/webhook", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    Console.WriteLine($"Received LINE Webhook: {body}");

    return Results.Ok();
});

app.Run();