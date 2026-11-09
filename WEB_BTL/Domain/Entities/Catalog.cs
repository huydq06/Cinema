using WEB_BTL.Domain.Enums;

namespace WEB_BTL.Domain.Entities;

public class Genre
{
    public int GenreId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<MovieGenre> MovieGenres { get; set; } = [];
}

public class Movie
{
    public int MovieId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string DescriptionHtml { get; set; } = string.Empty;
    public string? Director { get; set; }
    public string? Cast { get; set; }
    public int DurationMinutes { get; set; }
    public DateOnly ReleaseDate { get; set; }
    public string PosterUrl { get; set; } = string.Empty;
    public string? TrailerUrl { get; set; }
    public string AgeRating { get; set; } = string.Empty;
    public MoviePublicationStatus PublicationStatus { get; set; } = MoviePublicationStatus.Draft;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<MovieGenre> MovieGenres { get; set; } = [];
    public ICollection<Showtime> Showtimes { get; set; } = [];
}

public class MovieGenre
{
    public int MovieId { get; set; }
    public Movie? Movie { get; set; }
    public int GenreId { get; set; }
    public Genre? Genre { get; set; }
}
