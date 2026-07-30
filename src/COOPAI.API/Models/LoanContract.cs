using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using COOPAI.API.Common;

namespace COOPAI.API.Models;

/// <summary>
/// สัญญาสินเชื่อ
/// </summary>
public class LoanContract : BaseEntity
{
    /// <summary>
    /// เลขที่สัญญา
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string ContractNo { get; set; } = string.Empty;

    /// <summary>
    /// สมาชิกผู้กู้
    /// </summary>
    public int MemberId { get; set; }

    [ForeignKey(nameof(MemberId))]
    public virtual Member? Member { get; set; }

    /// <summary>
    /// ประเภทสินเชื่อ
    /// </summary>
    public int LoanTypeId { get; set; }

    [ForeignKey(nameof(LoanTypeId))]
    public virtual LoanType? LoanType { get; set; }

    /// <summary>
    /// วันที่ทำสัญญา
    /// </summary>
    public DateTime ContractDate { get; set; }

    /// <summary>
    /// วันครบกำหนด
    /// </summary>
    public DateTime? ExpireDate { get; set; }

    /// <summary>
    /// วงเงินกู้
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal LoanAmount { get; set; }

    /// <summary>
    /// เงินต้นคงเหลือ
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal PrincipalBalance { get; set; }

    /// <summary>
    /// กำไรค้างรับ
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal ProfitBalance { get; set; }

    /// <summary>
    /// ยอดหนี้รวม
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalBalance { get; set; }

    /// <summary>
    /// จำนวนวันค้างชำระ
    /// </summary>
    public int OverdueDays { get; set; }

    /// <summary>
    /// ปิดสัญญาแล้วหรือไม่
    /// </summary>
    public bool IsClosed { get; set; } = false;

    // -------------------------
    // Navigation Properties
    // -------------------------

    public virtual ICollection<Guarantor> Guarantors { get; set; }
        = new List<Guarantor>();

    public virtual ICollection<LoanPayment> LoanPayments { get; set; }
        = new List<LoanPayment>();
}