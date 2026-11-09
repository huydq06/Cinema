using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WEB_BTL.Domain.Entities;

namespace WEB_BTL.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments", t =>
        {
            t.HasCheckConstraint("CK_Payments_Amount", "[Amount] >= 0");
            t.HasCheckConstraint("CK_Payments_Refund",
                "[RefundAmount] >= 0 AND [RefundAmount] <= [Amount]");
            t.HasCheckConstraint("CK_Payments_Accepted",
                "[IsAccepted] = 0 OR [CapturedAtUtc] IS NOT NULL");
            t.HasCheckConstraint("CK_Payments_Captured",
                "[Status] NOT IN ('Succeeded','RefundPending','Refunded') OR [CapturedAtUtc] IS NOT NULL");
        });
        b.HasKey(x => x.PaymentId);
        b.Property(x => x.PaymentId).UseIdentityColumn();

        b.Property(x => x.Provider).HasColumnType("varchar(30)").IsRequired();
        b.Property(x => x.MerchantReference).HasColumnType("varchar(64)").IsRequired();
        b.HasIndex(x => x.MerchantReference).IsUnique();
        b.Property(x => x.ProviderTransactionId).HasColumnType("varchar(128)");
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Currency).HasColumnType("char(3)").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(24)").IsRequired();
        b.Property(x => x.IsAccepted).HasDefaultValue(false);

        b.Property(x => x.ClientRequestId).IsRequired();
        b.HasIndex(x => x.ClientRequestId).IsUnique();
        b.Property(x => x.RequestHash).HasColumnType("char(64)").IsRequired();

        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.CapturedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RefundAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        b.Property(x => x.RefundRequestedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RefundedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ProviderRefundReference).HasColumnType("varchar(128)");
        b.Property(x => x.FailureCode).HasColumnType("varchar(100)");
        b.Property(x => x.RowVersion).IsRowVersion();

        // Một lần thu tiền được chấp nhận cho mỗi đơn.
        b.HasIndex(x => x.BookingId)
            .HasDatabaseName("UX_Payments_OneAcceptedPerBooking")
            .IsUnique()
            .HasFilter("[IsAccepted] = 1");

        b.HasIndex(x => new { x.Provider, x.ProviderTransactionId })
            .HasDatabaseName("UX_Payments_ProviderTransaction")
            .IsUnique()
            .HasFilter("[ProviderTransactionId] IS NOT NULL");

        b.HasIndex(x => new { x.BookingId, x.CreatedAtUtc })
            .HasDatabaseName("IX_Payments_Booking_Created")
            .IsDescending(false, true);
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc })
            .HasDatabaseName("IX_Payments_Status_Created");

        b.HasOne(x => x.Booking).WithMany(x => x.Payments).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> b)
    {
        b.ToTable("PaymentEvents");
        b.HasKey(x => x.PaymentEventId);
        b.Property(x => x.PaymentEventId).UseIdentityColumn();

        b.Property(x => x.Provider).HasColumnType("varchar(30)").IsRequired();
        b.Property(x => x.EventKey).HasColumnType("varchar(128)").IsRequired();
        b.Property(x => x.PayloadHash).HasColumnType("char(64)").IsRequired();
        b.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ProcessedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ProcessingStatus).HasConversion<string>().HasColumnType("varchar(24)").IsRequired();
        b.Property(x => x.ResultCode).HasColumnType("varchar(100)");

        b.HasIndex(x => new { x.Provider, x.EventKey }).IsUnique();

        b.HasOne(x => x.Payment).WithMany(p => p.Events).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.AuditLogId);
        b.Property(x => x.AuditLogId).UseIdentityColumn();

        b.Property(x => x.Action).HasColumnType("varchar(100)").IsRequired();
        b.Property(x => x.EntityType).HasColumnType("varchar(50)").IsRequired();
        b.Property(x => x.EntityId).HasColumnType("varchar(64)").IsRequired();
        b.Property(x => x.OccurredAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.TraceId).HasColumnType("varchar(64)").IsRequired();
        b.Property(x => x.SummaryJson).HasColumnType("nvarchar(max)");

        b.HasIndex(x => x.OccurredAtUtc).IsDescending().HasDatabaseName("IX_AuditLogs_Occurred");
        b.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("IX_AuditLogs_Entity");

        b.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
