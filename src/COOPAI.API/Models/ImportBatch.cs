using System.ComponentModel.DataAnnotations;
using COOPAI.API.Common;

namespace COOPAI.API.Models;

/// <summary>
/// ประวัติการนำเข้าข้อมูล
/// </summary>
public class ImportBatch : BaseEntity
{
    /// <summary>
    /// ชื่อไฟล์
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// วันที่นำเข้า
    /// </summary>
    public DateTime ImportDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// จำนวนรายการทั้งหมด
    /// </summary>
    public int TotalRecords { get; set; }

    /// <summary>
    /// จำนวนรายการที่นำเข้าสำเร็จ
    /// </summary>
    public int SuccessRecords { get; set; }

    /// <summary>
    /// จำนวนรายการที่ผิดพลาด
    /// </summary>
    public int FailedRecords { get; set; }

    /// <summary>
    /// สถานะการนำเข้า
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// รายละเอียดข้อผิดพลาด
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// รายการ Log ของการนำเข้า
    /// </summary>
    public virtual ICollection<ImportLog> ImportLogs { get; set; }
        = new List<ImportLog>();
}