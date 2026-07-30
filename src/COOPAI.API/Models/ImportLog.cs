using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using COOPAI.API.Common;

namespace COOPAI.API.Models;

/// <summary>
/// บันทึกเหตุการณ์การ Import
/// </summary>
public class ImportLog : BaseEntity
{
    /// <summary>
    /// Import Batch
    /// </summary>
    public int ImportBatchId { get; set; }

    [ForeignKey(nameof(ImportBatchId))]
    public virtual ImportBatch? ImportBatch { get; set; }

    /// <summary>
    /// ลำดับแถวใน Excel
    /// </summary>
    public int RowNumber { get; set; }

    /// <summary>
    /// ระดับข้อความ
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Level { get; set; } = "Info";

    /// <summary>
    /// รายละเอียด
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;
}