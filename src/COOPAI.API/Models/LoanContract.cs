using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using COOPAI.API.Common;

namespace COOPAI.API.Models;

/// <summary>
/// สัญญาสินเชื่อ
/// </summary>
public class LoanContract : BaseEntity
{
    #region Contract

    [Required]
    [MaxLength(30)]
    public string ContractNo { get; set; } = string.Empty;

    public DateTime ContractDate { get; set; }

    public DateTime? ExpireDate { get; set; }

    public bool IsClosed { get; set; }

    #endregion

    #region Member

    public int MemberId { get; set; }

    public virtual Member? Member { get; set; }

    #endregion

    #region Loan Type

    public int LoanTypeId { get; set; }

    public virtual LoanType? LoanType { get; set; }

    #endregion

    #region Financial

    [Column(TypeName = "decimal(18,2)")]
    public decimal LoanAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PrincipalBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ProfitBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalBalance { get; set; }

    #endregion

    #region Collection

    public int InstallmentCount { get; set; }

    public int PaidInstallmentCount { get; set; }

    public int OverdueDays { get; set; }

    public DateTime? LastPaymentDate { get; set; }

    #endregion

    #region Follow Up

    public DateTime? LastFollowUpDate { get; set; }

    [MaxLength(200)]
    public string? LastFollowUpRemark { get; set; }

    #endregion

    #region Assignment

    public int? AssignedOfficerId { get; set; }

    #endregion

    #region Navigation

    public virtual ICollection<LoanPayment> LoanPayments { get; set; }
        = new List<LoanPayment>();

    public virtual ICollection<Guarantor> Guarantors { get; set; }
        = new List<Guarantor>();

    #endregion
}