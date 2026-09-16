using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.Tests;

public sealed class OfficerAssignmentPolicyTests
{
    [Theory]
    [InlineData("M", OfficerAssignmentPolicy.Tuaeso)]
    [InlineData("M15", OfficerAssignmentPolicy.Tuaeso)]
    [InlineData("L01", OfficerAssignmentPolicy.Tuaeso)]
    [InlineData("L02", OfficerAssignmentPolicy.Tuaeso)]
    [InlineData("R", OfficerAssignmentPolicy.Burhanuddin)]
    [InlineData("R99", OfficerAssignmentPolicy.Burhanuddin)]
    [InlineData("L04", OfficerAssignmentPolicy.Burhanuddin)]
    [InlineData("L30", OfficerAssignmentPolicy.Burhanuddin)]
    [InlineData("Y", OfficerAssignmentPolicy.Waesobri)]
    [InlineData("Y20", OfficerAssignmentPolicy.Waesobri)]
    [InlineData("C", OfficerAssignmentPolicy.Irifan)]
    [InlineData("C12", OfficerAssignmentPolicy.Irifan)]
    [InlineData("L32", OfficerAssignmentPolicy.Irifan)]
    [InlineData("L53", OfficerAssignmentPolicy.Irifan)]
    public void NonOverlappingRosterRules_AssignExactlyOneOfficer(string groupCode, string officer)
    {
        var result = OfficerAssignmentPolicy.Resolve(groupCode);

        Assert.Equal(OfficerAssignmentStatuses.Assigned, result.Status);
        Assert.Equal(officer, result.OfficerName);
        Assert.Single(result.OfficerCandidates);
    }

    [Theory]
    [InlineData("L03")]
    [InlineData(" l3 ")]
    [InlineData("L31")]
    public void OverlappingRosterRules_AreConflict_NotSilentlyGuessed(string groupCode)
    {
        var result = OfficerAssignmentPolicy.Resolve(groupCode);

        Assert.Equal(OfficerAssignmentStatuses.Conflict, result.Status);
        Assert.Null(result.OfficerName);
        Assert.Equal(2, result.OfficerCandidates.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("L54")]
    [InlineData("Z01")]
    public void UnsupportedOrMissingGroup_IsUnassigned(string? groupCode)
    {
        var result = OfficerAssignmentPolicy.Resolve(groupCode);

        Assert.Equal(OfficerAssignmentStatuses.Unassigned, result.Status);
        Assert.Null(result.OfficerName);
        Assert.Empty(result.OfficerCandidates);
    }
}