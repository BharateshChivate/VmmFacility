using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VmmFacility.Api;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

var port = int.TryParse(Environment.GetEnvironmentVariable("VMM_PORT"), out var p) ? p : 5180;
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Loopback only: the app is never reachable from the LAN.
    kestrel.Listen(IPAddress.Loopback, port);
    kestrel.Listen(IPAddress.IPv6Loopback, port);
});

var dataDir = AppPaths.DataDirectory();
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "vmm-facility.db");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddDataProtection()
    .SetApplicationName("VmmFacility")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "vmm_session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.None; // plain http on localhost
        o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

app.Logger.LogInformation("SQLite database: {DbPath}", dbPath);

app.UseDefaultFiles();
app.UseStaticFiles();

// State-changing API calls must carry a custom header. Browsers can't add it cross-origin
// without a CORS preflight, which this app never grants.
app.Use(async (ctx, next) =>
{
    var method = ctx.Request.Method;
    if (ctx.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)
        && ctx.Request.Headers["X-VMM-Request"] != "1")
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

var setupLock = new SemaphoreSlim(1, 1);

// ---- Auth ----

var auth = app.MapGroup("/api/auth");

auth.MapGet("/session", async (HttpContext ctx, AppDbContext db) =>
{
    var setupRequired = !await db.Users.AnyAsync();
    var signedIn = !setupRequired && ctx.User.Identity?.IsAuthenticated == true;
    return Results.Ok(new
    {
        setupRequired,
        signedIn,
        userName = signedIn ? ctx.User.Identity!.Name : null,
    });
});

auth.MapPost("/setup", async (CredentialsInput input, HttpContext ctx, AppDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var errors = new Dictionary<string, string[]>();
    var userName = input.UserName?.Trim() ?? "";
    var password = input.Password ?? "";
    if (userName.Length < 3 || userName.Length > 50)
        errors["userName"] = ["Username must be 3 to 50 characters."];
    if (password.Length < 8 || password.Length > 128)
        errors["password"] = ["Password must be 8 to 128 characters."];
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    await setupLock.WaitAsync();
    try
    {
        if (await db.Users.AnyAsync())
            return Results.Problem("An account already exists. Sign in instead.", statusCode: StatusCodes.Status409Conflict);

        var user = new AppUser { UserName = userName, CreatedAtUtc = DateTime.UtcNow };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await SignIn(ctx, user);
        return Results.Ok(new { userName = user.UserName });
    }
    finally
    {
        setupLock.Release();
    }
});

auth.MapPost("/login", async (CredentialsInput input, HttpContext ctx, AppDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var user = await db.Users.SingleOrDefaultAsync();
    var userName = input.UserName?.Trim() ?? "";
    var password = input.Password ?? "";

    var result = PasswordVerificationResult.Failed;
    if (user is not null && string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase))
        result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);

    if (user is null || result == PasswordVerificationResult.Failed)
        return Results.Problem("Username or password is incorrect.", statusCode: StatusCodes.Status401Unauthorized);

    if (result == PasswordVerificationResult.SuccessRehashNeeded)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        await db.SaveChangesAsync();
    }

    await SignIn(ctx, user);
    return Results.Ok(new { userName = user.UserName });
});

auth.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});

// ---- Expenses ----

var expenses = app.MapGroup("/api/expenses").RequireAuthorization();

expenses.MapGet("/", async (AppDbContext db) =>
{
    var list = await LoadSorted(db);
    return Results.Ok(list.Select(ExpenseDto.From));
});

expenses.MapGet("/{id:int}", async (int id, AppDbContext db) =>
    await db.Expenses.FindAsync(id) is { } e
        ? Results.Ok(ExpenseDto.From(e))
        : Results.Problem("This expense was not found.", statusCode: StatusCodes.Status404NotFound));

expenses.MapPost("/", async (ExpenseInput input, AppDbContext db) =>
{
    var errors = ExpenseRules.Validate(input, out var v);
    if (v is null)
        return Results.ValidationProblem(errors);

    var now = DateTime.UtcNow;
    var e = new Expense
    {
        Amount = v.Amount, Date = v.Date, Category = v.Category, Description = v.Description,
        PaymentState = v.PaymentState, CreatedAtUtc = now, UpdatedAtUtc = now,
    };
    db.Expenses.Add(e);
    await db.SaveChangesAsync();
    return Results.Created($"/api/expenses/{e.Id}", ExpenseDto.From(e));
});

expenses.MapPut("/{id:int}", async (int id, ExpenseInput input, AppDbContext db) =>
{
    var e = await db.Expenses.FindAsync(id);
    if (e is null)
        return Results.Problem("This expense was not found.", statusCode: StatusCodes.Status404NotFound);

    var errors = ExpenseRules.Validate(input, out var v);
    if (v is null)
        return Results.ValidationProblem(errors);

    e.Amount = v.Amount;
    e.Date = v.Date;
    e.Category = v.Category;
    e.Description = v.Description;
    e.PaymentState = v.PaymentState;
    e.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(ExpenseDto.From(e));
});

expenses.MapGet("/report.pdf", async (AppDbContext db) =>
{
    var list = await LoadSorted(db);
    var now = DateTime.Now;
    var pdf = ExpenseReport.Generate(list, now);
    return Results.File(pdf, "application/pdf", $"VMM-Expense-Report-{now:yyyy-MM-dd}.pdf");
});

// Unknown API routes return 404; everything else falls back to the React app.
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

static async Task<List<Expense>> LoadSorted(AppDbContext db) =>
    await db.Expenses.AsNoTracking()
        .OrderByDescending(e => e.Date)
        .ThenByDescending(e => e.Id)
        .ToListAsync();

static Task SignIn(HttpContext ctx, AppUser user)
{
    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.UserName)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    // Non-persistent cookie: ends when the browser closes, or after 30 idle minutes.
    return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = false });
}

record CredentialsInput(string? UserName, string? Password);
