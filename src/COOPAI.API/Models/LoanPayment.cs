using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using COOPAI.API.Common;

namespace COOPAI.API.Models;

/// <summary>
/// ประวัติการรับชำระหนี้
/// </summary>
public class LoanPayment : BaseEntity
{
    /// <summary>
    /// สัญญา
    /// </summary>
    public int LoanContractId { get; set; }

    [ForeignKey(nameof(LoanContractId))]
    public virtual LoanContract? LoanContract { get; set; }

    /// <summary>
    /// เลขที่ใบเสร็จ
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string ReceiptNo { get; set; } = string.Empty;

    /// <summary>
    /// วันที่รับชำระ
    /// </summary>
    public DateTime PaymentDate { get; set; }

    /// <summary>
    /// ชำระเงินต้น
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal PrincipalPaid { get; set; }

    /// <summary>
    /// ชำระกำไร
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal ProfitPaid { get; set; }

    /// <summary>
    /// รวมรับชำระ
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPaid { get; set; }

    /// <summary>
    /// เงินต้นคงเหลือหลังรับชำระ
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal PrincipalBalance { get; set; }

    /// <summary>
    /// กำไรคงเหลือหลังรับชำระ
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal ProfitBalance { get; set; }

    /// <summary>
    /// หนี้คงเหลือรวมหลังรับชำระ
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalBalance { get; set; }
}