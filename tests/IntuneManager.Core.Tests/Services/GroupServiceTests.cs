using IntuneManager.Core.Services;
using Microsoft.Graph.Models;

namespace IntuneManager.Core.Tests.Services;

public class GroupServiceTests
{
    [Theory]
    [InlineData(true, false, null, "Security")]
    [InlineData(true, true, null, "Mail-enabled Security")]
    [InlineData(false, true, null, "Distribution")]
    [InlineData(true, false, "Unified", "Microsoft 365")]
    [InlineData(false, false, null, "Security")]
    public void InferGroupType_ReturnsCorrectLabel(bool securityEnabled, bool mailEnabled, string? groupType, string expected)
    {
        var group = new Group
        {
            SecurityEnabled = securityEnabled,
            MailEnabled = mailEnabled,
            GroupTypes = groupType != null ? [groupType] : []
        };

        var result = GroupService.InferGroupType(group);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void InferGroupType_DynamicSecurityGroup_ReturnsSecurity()
    {
        var group = new Group
        {
            SecurityEnabled = true,
            MailEnabled = false,
            GroupTypes = ["DynamicMembership"]
        };

        var result = GroupService.InferGroupType(group);

        // A dynamic group that is security-enabled but not Unified → Security
        Assert.Equal("Security", result);
    }

    [Fact]
    public void InferGroupType_DynamicM365Group_ReturnsMicrosoft365()
    {
        var group = new Group
        {
            SecurityEnabled = false,
            MailEnabled = true,
            GroupTypes = ["Unified", "DynamicMembership"]
        };

        var result = GroupService.InferGroupType(group);

        Assert.Equal("Microsoft 365", result);
    }

    [Fact]
    public void InferGroupType_NullGroupTypes_ReturnsSecurity()
    {
        var group = new Group
        {
            SecurityEnabled = true,
            MailEnabled = false,
            GroupTypes = null
        };

        var result = GroupService.InferGroupType(group);

        Assert.Equal("Security", result);
    }

    [Fact]
    public void GroupMemberCounts_RecordProperties()
    {
        var counts = new GroupMemberCounts(10, 5, 2, 17);

        Assert.Equal(10, counts.Users);
        Assert.Equal(5, counts.Devices);
        Assert.Equal(2, counts.NestedGroups);
        Assert.Equal(17, counts.Total);
    }
}
