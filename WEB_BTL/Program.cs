using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WEB_BTL.Application.Options;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Infrastructure.Concurrency;
using WEB_BTL.Infrastructure.Data;
using WEB_BTL.Infrastructure.Payments;
using WEB_BTL.Infrastructure.QrCode;
using WEB_BTL.Infrastructure.Security;
using WEB_BTL.Infrastructure.Storage;
using WEB_BTL.Workers;

var builder = WebApplication.CreateBuilder(args);

// ---- Cấu hình strongly-typed -------------------------------------------------
builder.Services.Configure<BookingOptions>(builder.Configuration.GetSection(BookingOptions.SectionName));
builder.Services.Configure<ShowtimeOptions>(builder.Configuration.GetSection(ShowtimeOptions.SectionName));
builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection(PaymentOptions.SectionName));
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection(UploadOptions.SectionName));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));

// ---- Dữ liệu ----------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("CinemaDb")
    ?? throw new InvalidOperationException("Thiếu ConnectionStrings:CinemaDb.");

builder.Services.AddDbContext<CinemaDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.CommandTimeout(30)));

// ---- Xác thực, phân quyền, session -----------------------------------------
builder.Services.AddDataProtection()
    .SetApplicationName("WEB_BTL.Cinema");

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Cinema.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Cookie cũ bị thu hồi khi đổi mật khẩu, đổi role hoặc bị khóa tài khoản.
        options.Events.OnValidatePrincipal = async context =>
        {
            var principal = context.Principal;
            var rawUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var rawVersion = principal?.FindFirstValue(ClaimNames.SecurityVersion);
            var role = principal?.FindFirstValue(ClaimTypes.Role);

            if (!int.TryParse(rawUserId, out var userId)
                || !int.TryParse(rawVersion, out var securityVersion)
                || string.IsNullOrEmpty(role))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var accounts = context.HttpContext.RequestServices.GetRequiredService<IAccountService>();
            var stillValid = await accounts.IsStillValidAsync(userId, role, securityVersion, context.HttpContext.RequestAborted);
            if (!stillValid)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.CustomerBooking,
        p => p.RequireRole(Roles.Customer));

    options.AddPolicy(Policies.OperateCinema,
        p => p.RequireRole(Roles.Staff, Roles.Admin));

    options.AddPolicy(Policies.ManageCinema,
        p => p.RequireRole(Roles.Admin));

    options.AddPolicy(Policies.ViewRevenue,
        p => p.RequireRole(Roles.Admin));
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "Cinema.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(2);
});

builder.Services.AddHttpContextAccessor();

// ---- Dịch vụ nghiệp vụ ------------------------------------------------------
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IQrTokenService, QrTokenService>();
builder.Services.AddScoped<IQrTokenServiceHolder, QrTokenServiceHolder>();
builder.Services.AddScoped<IAppLockService, SqlAppLockService>();

builder.Services.AddScoped<IPaymentGateway, FakeGateway>();

builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IMovieCatalogService, MovieCatalogService>();
builder.Services.AddScoped<IShowtimeService, ShowtimeService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ICheckInService, CheckInService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminCatalogService, AdminCatalogService>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();

// ---- TMDB ---------------------------------------------------------------
builder.Services.AddHttpClient("tmdb", client =>
{
    var token = builder.Configuration["Tmdb:BearerToken"] ?? "";
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<ITmdbService, TmdbService>();

if (builder.Configuration.GetValue("Database:MigrateOnStartup", builder.Environment.IsDevelopment()))
{
    builder.Services.AddHostedService<DatabaseStartupWorker>();
}

builder.Services.AddHostedService<ExpireBookingsWorker>();
builder.Services.AddHostedService<PaymentReconciliationWorker>();

// ---- Web -------------------------------------------------------------------
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve ảnh upload từ thư mục uploads/
var uploadPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadPath),
    RequestPath = "/uploads"
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

/// <summary>Tên claim riêng của ứng dụng (không trùng claim chuẩn của Identity).</summary>
public static class ClaimNames
{
    public const string SecurityVersion = "cinema:sv";
    public const string UserId = "cinema:uid";
}
