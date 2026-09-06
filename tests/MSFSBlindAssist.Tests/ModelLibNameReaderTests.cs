using System.Text;
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class ModelLibNameReaderTests
{
    [Fact]
    public void Reads_guid_name_pairs_in_either_attribute_order_and_ignores_noise()
    {
        string xml =
            "\0\0<ModelInfo version=\"1.1\" guid=\"{416f6b5f-f52e-4744-858a-29067c13cdb0}\" name=\"KTIW_ATP_Hanger\"><LODS/></ModelInfo>\0\0" +
            "<ModelInfo guid=\"{8DC82808-32C8-4C75-8FC5-BBC825565B12}\" version=\"1.1\" name=\"concourse_a_02\"><LODS/></ModelInfo>" +
            "<ModelInfo name=\"tower_01\" version=\"1.1\" guid=\"{a611c36a-df97-4154-a1f1-65a8fbec9bd0}\"/>" +
            "<ModelInfo version=\"1.1\" name=\"no_guid\"/>";
        var names = ModelLibNameReader.Read(Encoding.Latin1.GetBytes(xml));
        Assert.Equal(3, names.Count);
        Assert.Equal("KTIW_ATP_Hanger", names[Guid.Parse("416f6b5f-f52e-4744-858a-29067c13cdb0")]);
        Assert.Equal("concourse_a_02", names[Guid.Parse("8dc82808-32c8-4c75-8fc5-bbc825565b12")]);
        Assert.Equal("tower_01", names[Guid.Parse("a611c36a-df97-4154-a1f1-65a8fbec9bd0")]);
    }
}
