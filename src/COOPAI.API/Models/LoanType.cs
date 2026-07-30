using System.ComponentModel.DataAnnotations;
using COOPAI.API.Common;

namespace COOPAI.API.Models;

/// <summary>
/// ประเภทเงินกู้
/// </summary>
public class LoanType : BaseEntity
{
    /// <summary>
    /// รหัสประเภทเงินกู้
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// ชื่อประเภทเงินกู้
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// รายละเอียด
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// ใช้งานอยู่หรือไม่
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// สัญญาเงินกู้
    /// </summary>
    public virtual ICollection<LoanContract> LoanContracts { get; set; }
        = new List<LoanContract>();
}