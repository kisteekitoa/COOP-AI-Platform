using System.ComponentModel.DataAnnotations;
using COOPAI.API.Common;

namespace COOPAI.API.Models;

/// <summary>
/// สมาชิกสหกรณ์
/// </summary>
public class Member : BaseEntity
{
    /// <summary>
    /// เลขทะเบียนสมาชิก
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string MemberNo { get; set; } = string.Empty;

    /// <summary>
    /// คำนำหน้า
    /// </summary>
    [MaxLength(20)]
    public string? Title { get; set; }

    /// <summary>
    /// ชื่อ
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// นามสกุล
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// ชื่อ-นามสกุล
    /// </summary>
    [Required]
    [MaxLength(250)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// รหัสกลุ่มสมาชิก
    /// </summary>
    [MaxLength(20)]
    public string? GroupCode { get; set; }

    /// <summary>
    /// สถานะสมาชิก
    /// </summary>
    public bool IsActive { get; set; } = true;

    // -------------------------
    // Navigation Properties
    // -------------------------

    public virtual ICollection<LoanContract> LoanContracts { get; set; }
        = new List<LoanContract>();

    public virtual ICollection<Guarantor> GuaranteedLoans { get; set; }
        = new List<Guarantor>();
}