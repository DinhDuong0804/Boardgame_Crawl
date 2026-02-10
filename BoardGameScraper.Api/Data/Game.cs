using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoardGameScraper.Api.Data.Entities;

/// <summary>
/// Entity đại diện cho một board game trong hệ thống
/// Dữ liệu được lấy từ BoardGameGeek (BGG)
/// </summary>
[Table("games")]
public class Game
{
    /// <summary>
    /// ID tự tăng trong database
    /// </summary>
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// ID của game trên BoardGameGeek
    /// </summary>
    [Column("bgg_id")]
    public int BggId { get; set; }

    /// <summary>
    /// Tên game (tiếng Anh gốc)
    /// </summary>
    [Column("name")]
    [MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Năm phát hành game
    /// </summary>
    [Column("year_published")]
    public int? YearPublished { get; set; }

    /// <summary>
    /// Mô tả chi tiết về game
    /// </summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Số người chơi tối thiểu
    /// </summary>
    [Column("min_players")]
    public int? MinPlayers { get; set; }

    /// <summary>
    /// Số người chơi tối đa
    /// </summary>
    [Column("max_players")]
    public int? MaxPlayers { get; set; }

    /// <summary>
    /// Thời gian chơi tối thiểu (phút)
    /// </summary>
    [Column("min_playtime")]
    public int? MinPlaytime { get; set; }

    /// <summary>
    /// Thời gian chơi tối đa (phút)
    /// </summary>
    [Column("max_playtime")]
    public int? MaxPlaytime { get; set; }

    /// <summary>
    /// Điểm đánh giá trung bình trên BGG (thang điểm 10)
    /// </summary>
    [Column("avg_rating")]
    public decimal? AvgRating { get; set; }

    /// <summary>
    /// Thứ hạng trên BGG (1 = cao nhất)
    /// </summary>
    [Column("bgg_rank")]
    public int? BggRank { get; set; }

    /// <summary>
    /// URL ảnh bìa game (độ phân giải cao)
    /// </summary>
    [Column("image_url")]
    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// URL ảnh thumbnail (độ phân giải thấp, dùng cho danh sách)
    /// </summary>
    [Column("thumbnail_url")]
    [MaxLength(1000)]
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Danh sách thể loại game (JSON array)
    /// VD: ["Strategy", "Family", "Card Game"]
    /// </summary>
    [Column("categories", TypeName = "jsonb")]
    public string Categories { get; set; } = "[]";

    /// <summary>
    /// Danh sách cơ chế chơi (JSON array)
    /// VD: ["Dice Rolling", "Hand Management", "Worker Placement"]
    /// </summary>
    [Column("mechanics", TypeName = "jsonb")]
    public string Mechanics { get; set; } = "[]";

    /// <summary>
    /// Danh sách nhà thiết kế game (JSON array)
    /// </summary>
    [Column("designers", TypeName = "jsonb")]
    public string Designers { get; set; } = "[]";

    /// <summary>
    /// Danh sách họa sĩ minh họa (JSON array)
    /// </summary>
    [Column("artists", TypeName = "jsonb")]
    public string Artists { get; set; } = "[]";

    /// <summary>
    /// Danh sách nhà phát hành (JSON array)
    /// </summary>
    [Column("publishers", TypeName = "jsonb")]
    public string Publishers { get; set; } = "[]";

    /// <summary>
    /// Trạng thái của game trong hệ thống
    /// Các giá trị: "scraped" (đã cào), "enriched" (đã bổ sung dữ liệu), "error" (lỗi)
    /// </summary>
    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "scraped";

    /// <summary>
    /// Thời điểm cào dữ liệu lần đầu
    /// </summary>
    [Column("scraped_at")]
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật dữ liệu lần cuối
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ==========================================
    // Navigation properties (Quan hệ với bảng khác)
    // ==========================================

    /// <summary>
    /// Danh sách rulebook/hướng dẫn chơi của game
    /// </summary>
    public ICollection<Rulebook> Rulebooks { get; set; } = new List<Rulebook>();

    /// <summary>
    /// Thông tin tồn kho tại quán cafe (nếu có)
    /// </summary>
    public CafeInventory? Inventory { get; set; }
}
