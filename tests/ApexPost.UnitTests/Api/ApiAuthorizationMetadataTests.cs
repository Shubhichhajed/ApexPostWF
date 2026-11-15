using System.Reflection;
using ApexPost.Api.Controllers;
using ApexPost.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace ApexPost.UnitTests.Api;

/// <summary>
/// JWT role rules declared on the actions. Reading the attributes is cheap and catches a removed or
/// mistyped role immediately; the real 401/403 behaviour is proved by ApexPost.Api.IntegrationTests.
/// </summary>
public sealed class ApiAuthorizationMetadataTests
{
    [Theory]
    [InlineData(typeof(ShipmentsController), nameof(ShipmentsController.GetShipment))]
    [InlineData(typeof(ShipmentsV2Controller), nameof(ShipmentsV2Controller.GetShipment))]
    [InlineData(typeof(ShipmentsController), nameof(ShipmentsController.GetShipments))]
    [InlineData(typeof(ShipmentsV2Controller), nameof(ShipmentsV2Controller.GetShipments))]
    [InlineData(typeof(ShipmentsV2Controller), nameof(ShipmentsV2Controller.GetScanHistory))]
    public void Authorize_ReadAction_RequiresAuthenticationWithoutRole(Type controller, string action)
    {
        // Act
        var authorize = GetAuthorize(controller, action);

        // Assert
        Assert.NotNull(authorize);
        Assert.Null(authorize.Roles);
    }

    [Theory]
    [InlineData(typeof(ShipmentsController), nameof(ShipmentsController.UpdateShipmentStatus), "Courier")]
    [InlineData(typeof(ShipmentsV2Controller), nameof(ShipmentsV2Controller.UpdateShipmentStatus), "Courier")]
    [InlineData(typeof(ShipmentsController), nameof(ShipmentsController.UploadScans), "Courier")]
    [InlineData(typeof(ShipmentsV2Controller), nameof(ShipmentsV2Controller.UploadScans), "Courier")]
    [InlineData(typeof(ShipmentsController), nameof(ShipmentsController.DeleteShipment), "Admin")]
    [InlineData(typeof(ShipmentsV2Controller), nameof(ShipmentsV2Controller.DeleteShipment), "Admin")]
    [InlineData(typeof(ShipmentsController), nameof(ShipmentsController.CreateShipment), "Admin")]
    [InlineData(typeof(ShipmentsV2Controller), nameof(ShipmentsV2Controller.CreateShipment), "Admin")]
    [InlineData(typeof(ShipmentsController), nameof(ShipmentsController.UpdateShipment), "Admin")]
    [InlineData(typeof(ShipmentsV2Controller), nameof(ShipmentsV2Controller.UpdateShipment), "Admin")]
    public void Authorize_MutatingAction_RequiresExactRole(Type controller, string action, string expectedRole)
    {
        // Act
        var authorize = GetAuthorize(controller, action);

        // Assert
        Assert.NotNull(authorize);
        Assert.Equal(expectedRole, authorize.Roles);
    }

    [Fact]
    public void PartnerController_Attributes_AnonymousForJwtButMarkedForApiKey()
    {
        // Arrange
        var controller = typeof(PartnerShipmentsController);

        // Act
        var anonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>();
        var apiKey = controller.GetCustomAttribute<RequirePartnerApiKeyAttribute>();

        // Assert
        Assert.NotNull(anonymous);
        Assert.NotNull(apiKey);
    }

    [Theory]
    [InlineData(typeof(ShipmentsController), true)]
    [InlineData(typeof(ShipmentsV2Controller), false)]
    public void ApiVersion_Controllers_OnlyV1IsDeprecated(Type controller, bool expectedDeprecated)
    {
        // Act
        var version = controller.GetCustomAttribute<Asp.Versioning.ApiVersionAttribute>();

        // Assert
        Assert.NotNull(version);
        Assert.Equal(expectedDeprecated, version.Deprecated);
    }

    private static AuthorizeAttribute? GetAuthorize(Type controllerType, string actionName)
    {
        var method = controllerType.GetMethod(actionName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return method.GetCustomAttribute<AuthorizeAttribute>();
    }
}
