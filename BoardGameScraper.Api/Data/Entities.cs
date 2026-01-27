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
    public GameTranslation? Translation { get; set; }
    public ICollection<Rulebook> Rulebooks { get; set; } = new List<Rulebook>();
    public CafeInventory? Inventory { get; set; }
}

[Table("game_translations")]
public class GameTranslation
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("game_id")]
    public int GameId { get; set; }

    [Column("name_vi")]
    [MaxLength(500)]
    public string? NameVi { get; set; }

    [Column("description_vi")]
    public string? DescriptionVi { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "pending";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("requested_at")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    // Navigation
    [ForeignKey("GameId")]
    public Game Game { get; set; } = null!;
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

    [Column("content_en")]
    public string? ContentEn { get; set; }

    [Column("content_vi")]
    public string? ContentVi { get; set; }

    [Column("markdown_path")]
    [MaxLength(500)]
    public string? MarkdownPath { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "pending";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

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

[Table("translation_queue")]
public class TranslationQueueItem
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("game_id")]
    public int? GameId { get; set; }

    [Column("rulebook_id")]
    public int? RulebookId { get; set; }

    [Column("request_type")]
    [MaxLength(50)]
    public string RequestType { get; set; } = "game_info";

    [Column("message_id")]
    public Guid MessageId { get; set; } = Guid.NewGuid();

    [Column("correlation_id")]
    public Guid? CorrelationId { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "queued";

    [Column("priority")]
    public int Priority { get; set; } = 0;

    [Column("retry_count")]
    public int RetryCount { get; set; } = 0;

    [Column("max_retries")]
    public int MaxRetries { get; set; } = 3;

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("queued_at")]
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    // Navigation
    [ForeignKey("GameId")]
    public Game? Game { get; set; }

    [ForeignKey("RulebookId")]
    public Rulebook? Rulebook { get; set; }
}
