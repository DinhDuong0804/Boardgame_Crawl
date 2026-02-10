using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoardGameScraper.Api.Data.Entities;

/// <summary>
/// Entity quản lý tồn kho game tại quán cafe
/// Mỗi game chỉ có một record inventory
/// </summary>
[Table("cafe_inventory")]
public class CafeInventory
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
    /// Tổng số lượng bản copy của game
    /// </summary>
    [Column("quantity")]
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Số bản copy đang còn sẵn sàng cho thuê
    /// </summary>
    [Column("available")]
    public int Available { get; set; } = 1;

    /// <summary>
    /// Vị trí lưu trữ game trong quán
    /// VD: "Kệ A2", "Tủ Strategy", "Quầy bar"
    /// </summary>
    [Column("location")]
    [MaxLength(100)]
    public string? Location { get; set; }

    /// <summary>
    /// Tình trạng game
    /// Các giá trị: "new" (mới), "good" (tốt), "fair" (trung bình), "poor" (kém)
    /// </summary>
    [Column("condition")]
    [MaxLength(50)]
    public string Condition { get; set; } = "good";

    /// <summary>
    /// Ghi chú thêm về game
    /// VD: "Thiếu 2 quân", "Đã sleeve", "Limited edition"
    /// </summary>
    [Column("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Thời điểm nhập game vào kho
    /// </summary>
    [Column("added_at")]
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm cập nhật thông tin lần cuối
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ==========================================
    // Navigation property
    // ==========================================

    /// <summary>
    /// Game mà record inventory này thuộc về
    /// </summary>
    [ForeignKey("GameId")]
    public Game Game { get; set; } = null!;
}
