using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WEB_BTL.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FoodCombos",
                columns: table => new
                {
                    ComboId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodCombos", x => x.ComboId);
                    table.CheckConstraint("CK_FoodCombos_Price", "[Price] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    GenreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(120)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.GenreId);
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    MovieId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(240)", nullable: false),
                    DescriptionHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Director = table.Column<string>(type: "nvarchar(150)", nullable: true),
                    Cast = table.Column<string>(type: "nvarchar(1000)", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PosterUrl = table.Column<string>(type: "nvarchar(500)", nullable: false),
                    TrailerUrl = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    AgeRating = table.Column<string>(type: "varchar(20)", nullable: false),
                    PublicationStatus = table.Column<string>(type: "varchar(24)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.MovieId);
                    table.CheckConstraint("CK_Movies_Duration", "[DurationMinutes] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "varchar(30)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    RoomId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomName = table.Column<string>(type: "nvarchar(50)", nullable: false),
                    RoomType = table.Column<string>(type: "varchar(20)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.RoomId);
                });

            migrationBuilder.CreateTable(
                name: "MovieGenres",
                columns: table => new
                {
                    MovieId = table.Column<int>(type: "int", nullable: false),
                    GenreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieGenres", x => new { x.MovieId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_MovieGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "GenreId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovieGenres_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "MovieId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(254)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", nullable: false),
                    Phone = table.Column<string>(type: "varchar(20)", nullable: true),
                    AvatarUrl = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SecurityVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LockoutEndUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.CheckConstraint("CK_Users_SecurityVersion", "[SecurityVersion] > 0");
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Seats",
                columns: table => new
                {
                    SeatId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    SeatCode = table.Column<string>(type: "varchar(10)", nullable: false),
                    RowIndex = table.Column<int>(type: "int", nullable: false),
                    ColIndex = table.Column<int>(type: "int", nullable: false),
                    SeatType = table.Column<string>(type: "varchar(20)", nullable: false),
                    PriceSurcharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seats", x => x.SeatId);
                    table.UniqueConstraint("AK_Seats_SeatId_RoomId", x => new { x.SeatId, x.RoomId });
                    table.CheckConstraint("CK_Seats_ColIndex", "[ColIndex] > 0");
                    table.CheckConstraint("CK_Seats_RowIndex", "[RowIndex] > 0");
                    table.CheckConstraint("CK_Seats_Surcharge", "[PriceSurcharge] >= 0");
                    table.ForeignKey(
                        name: "FK_Seats_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Showtimes",
                columns: table => new
                {
                    ShowtimeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MovieId = table.Column<int>(type: "int", nullable: false),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CleaningMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 15),
                    CleanupEndsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SalesCloseAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    BasePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Showtimes", x => x.ShowtimeId);
                    table.UniqueConstraint("AK_Showtimes_ShowtimeId_RoomId", x => new { x.ShowtimeId, x.RoomId });
                    table.CheckConstraint("CK_Showtimes_BasePrice", "[BasePrice] >= 0");
                    table.CheckConstraint("CK_Showtimes_Cleaning", "[CleaningMinutes] BETWEEN 0 AND 60");
                    table.CheckConstraint("CK_Showtimes_SalesClose", "[SalesCloseAtUtc] <= [StartAtUtc]");
                    table.CheckConstraint("CK_Showtimes_Time", "[StartAtUtc] < [EndAtUtc] AND [EndAtUtc] <= [CleanupEndsAtUtc]");
                    table.ForeignKey(
                        name: "FK_Showtimes_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "MovieId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Showtimes_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "varchar(100)", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(50)", nullable: false),
                    EntityId = table.Column<string>(type: "varchar(64)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    TraceId = table.Column<string>(type: "varchar(64)", nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditLogId);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    BookingId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingCode = table.Column<string>(type: "varchar(32)", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "varchar(10)", nullable: false),
                    ShowtimeId = table.Column<int>(type: "int", nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(254)", nullable: true),
                    ContactPhone = table.Column<string>(type: "varchar(20)", nullable: true),
                    MovieTitleSnapshot = table.Column<string>(type: "nvarchar(200)", nullable: false),
                    RoomNameSnapshot = table.Column<string>(type: "nvarchar(50)", nullable: false),
                    ShowStartAtUtcSnapshot = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TotalTicketAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalComboAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FinalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.BookingId);
                    table.UniqueConstraint("AK_Bookings_BookingId_ShowtimeId", x => new { x.BookingId, x.ShowtimeId });
                    table.CheckConstraint("CK_Bookings_Amounts", "[TotalTicketAmount] >= 0 AND [TotalComboAmount] >= 0 AND [FinalAmount] >= 0");
                    table.CheckConstraint("CK_Bookings_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_Bookings_OnlineUser", "([Channel] = 'Online' AND [UserId] IS NOT NULL AND [CreatedByUserId] = [UserId]) OR ([Channel] = 'POS')");
                    table.CheckConstraint("CK_Bookings_Total", "[FinalAmount] = [TotalTicketAmount] + [TotalComboAmount]");
                    table.ForeignKey(
                        name: "FK_Bookings_Showtimes_ShowtimeId",
                        column: x => x.ShowtimeId,
                        principalTable: "Showtimes",
                        principalColumn: "ShowtimeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShowtimeSeats",
                columns: table => new
                {
                    ShowtimeId = table.Column<int>(type: "int", nullable: false),
                    SeatId = table.Column<int>(type: "int", nullable: false),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    SeatCodeSnapshot = table.Column<string>(type: "varchar(10)", nullable: false),
                    SeatTypeSnapshot = table.Column<string>(type: "varchar(20)", nullable: false),
                    RowIndexSnapshot = table.Column<int>(type: "int", nullable: false),
                    ColIndexSnapshot = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    State = table.Column<string>(type: "varchar(24)", nullable: false),
                    BookingId = table.Column<long>(type: "bigint", nullable: true),
                    HeldUntilUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShowtimeSeats", x => new { x.ShowtimeId, x.SeatId });
                    table.CheckConstraint("CK_ShowtimeSeats_Price", "[Price] >= 0");
                    table.CheckConstraint("CK_ShowtimeSeats_State", "([State] IN ('Available','Blocked') AND [BookingId] IS NULL AND [HeldUntilUtc] IS NULL) OR ([State] = 'Held' AND [BookingId] IS NOT NULL AND [HeldUntilUtc] IS NOT NULL) OR ([State] = 'Sold' AND [BookingId] IS NOT NULL AND [HeldUntilUtc] IS NULL)");
                    table.ForeignKey(
                        name: "FK_ShowtimeSeats_Seats_SeatId_RoomId",
                        columns: x => new { x.SeatId, x.RoomId },
                        principalTable: "Seats",
                        principalColumns: new[] { "SeatId", "RoomId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShowtimeSeats_Showtimes_ShowtimeId_RoomId",
                        columns: x => new { x.ShowtimeId, x.RoomId },
                        principalTable: "Showtimes",
                        principalColumns: new[] { "ShowtimeId", "RoomId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingCombos",
                columns: table => new
                {
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    ComboId = table.Column<int>(type: "int", nullable: false),
                    ComboNameSnapshot = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingCombos", x => new { x.BookingId, x.ComboId });
                    table.CheckConstraint("CK_BookingCombos_Price", "[UnitPrice] >= 0");
                    table.CheckConstraint("CK_BookingCombos_Quantity", "[Quantity] BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_BookingCombos_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingCombos_FoodCombos_ComboId",
                        column: x => x.ComboId,
                        principalTable: "FoodCombos",
                        principalColumn: "ComboId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    PaymentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "varchar(30)", nullable: false),
                    MerchantReference = table.Column<string>(type: "varchar(64)", nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "varchar(128)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", nullable: false),
                    IsAccepted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    RefundRequestedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RefundedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ProviderRefundReference = table.Column<string>(type: "varchar(128)", nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(100)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.PaymentId);
                    table.CheckConstraint("CK_Payments_Accepted", "[IsAccepted] = 0 OR [CapturedAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_Payments_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_Payments_Captured", "[Status] NOT IN ('Succeeded','RefundPending','Refunded') OR [CapturedAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_Payments_Refund", "[RefundAmount] >= 0 AND [RefundAmount] <= [Amount]");
                    table.ForeignKey(
                        name: "FK_Payments_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingTickets",
                columns: table => new
                {
                    TicketId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    ShowtimeId = table.Column<int>(type: "int", nullable: false),
                    SeatId = table.Column<int>(type: "int", nullable: false),
                    SeatCodeSnapshot = table.Column<string>(type: "varchar(10)", nullable: false),
                    SeatTypeSnapshot = table.Column<string>(type: "varchar(20)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", nullable: false),
                    QrTokenHash = table.Column<string>(type: "char(64)", nullable: true),
                    QrTokenProtected = table.Column<string>(type: "nvarchar(1000)", nullable: true),
                    CheckedInAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CheckedInByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingTickets", x => x.TicketId);
                    table.CheckConstraint("CK_BookingTickets_CheckIn", "([CheckedInAtUtc] IS NULL AND [CheckedInByUserId] IS NULL) OR ([CheckedInAtUtc] IS NOT NULL AND [CheckedInByUserId] IS NOT NULL AND [Status] = 'Issued')");
                    table.CheckConstraint("CK_BookingTickets_Price", "[UnitPrice] >= 0");
                    table.CheckConstraint("CK_BookingTickets_Qr", "([Status] = 'Issued' AND [QrTokenHash] IS NOT NULL AND [QrTokenProtected] IS NOT NULL) OR ([Status] <> 'Issued' AND [QrTokenHash] IS NULL AND [QrTokenProtected] IS NULL)");
                    table.ForeignKey(
                        name: "FK_BookingTickets_Bookings_BookingId_ShowtimeId",
                        columns: x => new { x.BookingId, x.ShowtimeId },
                        principalTable: "Bookings",
                        principalColumns: new[] { "BookingId", "ShowtimeId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingTickets_ShowtimeSeats_ShowtimeId_SeatId",
                        columns: x => new { x.ShowtimeId, x.SeatId },
                        principalTable: "ShowtimeSeats",
                        principalColumns: new[] { "ShowtimeId", "SeatId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    PaymentEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "varchar(30)", nullable: false),
                    EventKey = table.Column<string>(type: "varchar(128)", nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ProcessingStatus = table.Column<string>(type: "varchar(24)", nullable: false),
                    ResultCode = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.PaymentEventId);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "PaymentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Entity",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Occurred",
                table: "AuditLogs",
                column: "OccurredAtUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_BookingCombos_ComboId",
                table: "BookingCombos",
                column: "ComboId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingCode",
                table: "Bookings",
                column: "BookingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ClientRequestId",
                table: "Bookings",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CreatedByUserId",
                table: "Bookings",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ShowtimeId",
                table: "Bookings",
                column: "ShowtimeId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status_Expires",
                table: "Bookings",
                columns: new[] { "Status", "ExpiresAtUtc" })
                .Annotation("SqlServer:Include", new[] { "ShowtimeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_User_Created",
                table: "Bookings",
                columns: new[] { "UserId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Bookings_OnePendingPerUserShowtime",
                table: "Bookings",
                columns: new[] { "UserId", "ShowtimeId" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [Status] = 'PendingPayment'");

            migrationBuilder.CreateIndex(
                name: "IX_BookingTickets_BookingId_SeatId",
                table: "BookingTickets",
                columns: new[] { "BookingId", "SeatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingTickets_BookingId_ShowtimeId",
                table: "BookingTickets",
                columns: new[] { "BookingId", "ShowtimeId" });

            migrationBuilder.CreateIndex(
                name: "UX_BookingTickets_IssuedSeat",
                table: "BookingTickets",
                columns: new[] { "ShowtimeId", "SeatId" },
                unique: true,
                filter: "[Status] = 'Issued'");

            migrationBuilder.CreateIndex(
                name: "UX_BookingTickets_QrTokenHash",
                table: "BookingTickets",
                column: "QrTokenHash",
                unique: true,
                filter: "[QrTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Slug",
                table: "Genres",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieGenres_GenreId",
                table: "MovieGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_PublicationStatus_ReleaseDate",
                table: "Movies",
                columns: new[] { "PublicationStatus", "ReleaseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_Slug",
                table: "Movies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PaymentId",
                table: "PaymentEvents",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_Provider_EventKey",
                table: "PaymentEvents",
                columns: new[] { "Provider", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Booking_Created",
                table: "Payments",
                columns: new[] { "BookingId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ClientRequestId",
                table: "Payments",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantReference",
                table: "Payments",
                column: "MerchantReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RecordedByUserId",
                table: "Payments",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_Created",
                table: "Payments",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Payments_OneAcceptedPerBooking",
                table: "Payments",
                column: "BookingId",
                unique: true,
                filter: "[IsAccepted] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_ProviderTransaction",
                table: "Payments",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true,
                filter: "[ProviderTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_RoleName",
                table: "Roles",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_RoomName",
                table: "Rooms",
                column: "RoomName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seats_RoomId_RowIndex_ColIndex",
                table: "Seats",
                columns: new[] { "RoomId", "RowIndex", "ColIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seats_RoomId_SeatCode",
                table: "Seats",
                columns: new[] { "RoomId", "SeatCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Showtimes_Movie_Start",
                table: "Showtimes",
                columns: new[] { "MovieId", "StartAtUtc" })
                .Annotation("SqlServer:Include", new[] { "Status", "SalesCloseAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Showtimes_Room_Start",
                table: "Showtimes",
                columns: new[] { "RoomId", "StartAtUtc" })
                .Annotation("SqlServer:Include", new[] { "CleanupEndsAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ShowtimeSeats_SeatId_RoomId",
                table: "ShowtimeSeats",
                columns: new[] { "SeatId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShowtimeSeats_Showtime_State",
                table: "ShowtimeSeats",
                columns: new[] { "ShowtimeId", "State", "HeldUntilUtc" })
                .Annotation("SqlServer:Include", new[] { "BookingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShowtimeSeats_ShowtimeId_RoomId",
                table: "ShowtimeSeats",
                columns: new[] { "ShowtimeId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BookingCombos");

            migrationBuilder.DropTable(
                name: "BookingTickets");

            migrationBuilder.DropTable(
                name: "MovieGenres");

            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropTable(
                name: "FoodCombos");

            migrationBuilder.DropTable(
                name: "ShowtimeSeats");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Seats");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "Showtimes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
