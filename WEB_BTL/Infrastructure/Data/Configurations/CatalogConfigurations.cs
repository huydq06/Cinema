using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WEB_BTL.Domain.Entities;

namespace WEB_BTL.Infrastructure.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.RoleId);
        b.Property(x => x.RoleId).UseIdentityColumn();
        b.Property(x => x.RoleName).HasColumnType("varchar(30)").IsRequired();
        b.HasIndex(x => x.RoleName).IsUnique();
        b.Property(x => x.Description).HasColumnType("nvarchar(200)");
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users", t => t.HasCheckConstraint("CK_Users_SecurityVersion", "[SecurityVersion] > 0"));
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).UseIdentityColumn();

        b.Property(x => x.FullName).HasColumnType("nvarchar(100)").IsRequired();
        b.Property(x => x.Email).HasColumnType("nvarchar(254)").IsRequired();
        b.Property(x => x.NormalizedEmail).HasColumnType("nvarchar(254)").IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Property(x => x.PasswordHash).HasColumnType("nvarchar(512)").IsRequired();
        b.Property(x => x.Phone).HasColumnType("varchar(20)");
        b.Property(x => x.AvatarUrl).HasColumnType("nvarchar(500)");
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.SecurityVersion).HasDefaultValue(1);
        b.Property(x => x.FailedLoginCount).HasDefaultValue(0);
        b.Property(x => x.LockoutEndUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Role).WithMany(r => r.Users).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> b)
    {
        b.ToTable("Genres");
        b.HasKey(x => x.GenreId);
        b.Property(x => x.GenreId).UseIdentityColumn();
        b.Property(x => x.Name).HasColumnType("nvarchar(100)").IsRequired();
        b.Property(x => x.Slug).HasColumnType("varchar(120)").IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.IsActive).HasDefaultValue(true);
    }
}

public class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> b)
    {
        b.ToTable("Movies", t =>
            t.HasCheckConstraint("CK_Movies_Duration", "[DurationMinutes] > 0"));
        b.HasKey(x => x.MovieId);
        b.Property(x => x.MovieId).UseIdentityColumn();

        b.Property(x => x.Title).HasColumnType("nvarchar(200)").IsRequired();
        b.Property(x => x.Slug).HasColumnType("varchar(240)").IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.DescriptionHtml).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Director).HasColumnType("nvarchar(150)");
        b.Property(x => x.Cast).HasColumnType("nvarchar(1000)");
        b.Property(x => x.ReleaseDate).HasColumnType("date");
        b.Property(x => x.PosterUrl).HasColumnType("nvarchar(500)").IsRequired();
        b.Property(x => x.TrailerUrl).HasColumnType("nvarchar(500)");
        b.Property(x => x.AgeRating).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.PublicationStatus).HasConversion<string>().HasColumnType("varchar(24)").IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.PublicationStatus, x.ReleaseDate });
    }
}

public class MovieGenreConfiguration : IEntityTypeConfiguration<MovieGenre>
{
    public void Configure(EntityTypeBuilder<MovieGenre> b)
    {
        b.ToTable("MovieGenres");
        b.HasKey(x => new { x.MovieId, x.GenreId });
        b.HasOne(x => x.Movie).WithMany(m => m.MovieGenres).HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Genre).WithMany(g => g.MovieGenres).HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.Restrict);
    }
}
