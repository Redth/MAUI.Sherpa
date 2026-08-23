using System.Security.Cryptography.Pkcs;
using Claunia.PropertyList;

namespace MauiSherpa.Core.Services;

internal static class ProvisioningProfileMetadata
{
    public static bool ProvisionsAllDevices(byte[] content)
    {
        var signedCms = new SignedCms();
        signedCms.Decode(content);

        var propertyList = PropertyListParser.Parse(signedCms.ContentInfo.Content) as NSDictionary;
        return propertyList?.TryGetValue("ProvisionsAllDevices", out var value) == true &&
               value is NSNumber number &&
               number.ToBool();
    }
}
