using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoardGameScraper.Api.Data.Entities;

/// <summary>
/// Entity lưu thông tin về rulebook/hướng dẫn chơi của game
/// Rulebook được lấy từ trang Files của BGG
/// </summary>
[Table("rulebooks")]
public class Rulebook
{
    /// <summary>
    /// ID tự tăng trong database
    /// </summary>
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// ID của game trong bảng games (Foreign Key)
    /// </summary>
    [Column("game_id")]
    public int GameId { get; set; }

    /// <summary>
    /// ID file trên BGG (dùng để tạo URL download)
    /// </summary>
    [Column("bgg_file_id")]
    [MaxLength(100)]
    public string? BggFileId { get; set; }

    /// <summary>
    /// Tiêu đề của rulebook
    /// VD: "English Rules", "Quick Start Guide"
    /// </summary>
    [Column("title")]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// URL gốc của file trên BGG
    /// </summary>
    [Column("original_url")]
    [MaxLength(1000)]
    public string OriginalUrl { get; set; } = string.Empty;

    /// <summary>
    /// Loại file: "pdf", "doc", "jpg", etc.
    /// </summary>
    [Column("file_type")]
    [MaxLength(20)]
    public string FileType { get; set; } = "pdf";

    /// <summary>
    /// Ngôn ngữ của rulebook
    /// VD: "English", "Vietnamese", "German"
    /// </summary>
    [Column("language")]
    [MaxLength(50)]
    public string Language { get; set; } = "English";

    /// <summary>
    /// Đường dẫn file local sau khi đã download
    /// VD: "output/pdfs/123_rules.pdf"
    /// </summary>
    [Column("local_file_path")]
    [MaxLength(500)]
    public string? LocalFilePath { get; set; }

    /// <summary>
    /// Trạng thái xử lý rulebook
    /// Các giá trị: "scraped" (đã cào link), "downloaded" (đã tải), "error" (lỗi)
    /// </summary>
    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "scraped";

    /// <summary>
    /// Thời điểm tạo record
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ==========================================
    // Navigation property
    // ==========================================

    /// <summary>
    /// Game mà rulebook này thuộc về
    /// </summary>
    [ForeignKey("GameId")]
    public Game Game { get; set; } = null!;
}
