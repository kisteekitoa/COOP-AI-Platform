using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using COOPAI.API.Common;

namespace COOPAI.API.Models;

/// <summary>
/// ผู้ค้ำประกัน
/// </summary>
public class Guarantor : BaseEntity
{
    /// <summary>
    /// สัญญาเงินกู้
    /// </summary>
    public int LoanContractId { get; set; }

    [ForeignKey(nameof(LoanContractId))]
    public virtual LoanContract? LoanContract { get; set; }

    /// <summary>
    /// รหัสสมาชิกผู้ค้ำ
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string MemberNo { get; set; } = string.Empty;

    /// <summary>
    /// ชื่อผู้ค้ำ
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// ลำดับผู้ค้ำ
    /// </summary>
    public int Sequence { get; set; }
}