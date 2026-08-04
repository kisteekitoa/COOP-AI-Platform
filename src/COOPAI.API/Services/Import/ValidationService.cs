namespace COOPAI.API.Services.Import;

public class ValidationService
{
    public bool IsValidMemberNo(string? memberNo)
    {
        return !string.IsNullOrWhiteSpace(memberNo);
    }

    public bool IsValidContractNo(string? contractNo)
    {
        return !string.IsNullOrWhiteSpace(contractNo);
    }
}