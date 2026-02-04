using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoardGameScraper.Api.Data.Entities;

[Table("games")]
public class Game
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("bgg_id")]
    public int BggId { get; set; }

    [Column("name")]
    [MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    [Column("year_published")]
    public int? YearPublished { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("min_players")]
    public int? MinPlayers { get; set; }

    [Column("max_players")]
    public int? MaxPlayers { get; set; }

    [Column("min_playtime")]
    public int? MinPlaytime { get; set; }

    [Column("max_playtime")]
    public int? MaxPlaytime { get; set; }

    [Column("avg_rating")]
    public decimal? AvgRating { get; set; }

    [Column("bgg_rank")]
    public int? BggRank { get; set; }

    [Column("image_url")]
    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    [Column("thumbnail_url")]
    [MaxLength(1000)]
    public string? ThumbnailUrl { get; set; }

    [Column("categories", TypeName = "jsonb")]
    public string Categories { get; set; } = "[]";

    [Column("mechanics", TypeName = "jsonb")]
    public string Mechanics { get; set; } = "[]";

    [Column("designers", TypeName = "jsonb")]
    public string Designers { get; set; } = "[]";

    [Column("artists", TypeName = "jsonb")]
    public string Artists { get; set; } = "[]";

    [Column("publishers", TypeName = "jsonb")]
    public string Publishers { get; set; } = "[]";

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "scraped";

    [Column("scraped_at")]
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<Rulebook> Rulebooks { get; set; } = new List<Rulebook>();
    public CafeInventory? Inventory { get; set; }
}

[Table("rulebooks")]
public class Rulebook
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("game_id")]
    public int GameId { get; set; }

    [Column("bgg_file_id")]
    [MaxLength(100)]
    public string? BggFileId { get; set; }

    [Column("title")]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [Column("original_url")]
    [MaxLength(1000)]
    public string OriginalUrl { get; set; } = string.Empty;

    [Column("file_type")]
    [MaxLength(20)]
    public string FileType { get; set; } = "pdf";

    [Column("language")]
    [MaxLength(50)]
    public string Language { get; set; } = "English";

    [Column("local_file_path")]
    [MaxLength(500)]
    public string? LocalFilePath { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "scraped";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("GameId")]
    public Game Game { get; set; } = null!;
}

[Table("cafe_inventory")]
public class CafeInventory
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("game_id")]
    public int GameId { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; } = 1;

    [Column("available")]
    public int Available { get; set; } = 1;

    [Column("location")]
    [MaxLength(100)]
    public string? Location { get; set; }

    [Column("condition")]
    [MaxLength(50)]
    public string Condition { get; set; } = "good";

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("added_at")]
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("GameId")]
    public Game Game { get; set; } = null!;
}
