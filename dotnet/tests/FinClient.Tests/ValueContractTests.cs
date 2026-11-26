using System.Text.Json;
using Fin.Copy;
using Xunit;

namespace Fin.Tests;

public sealed class ValueContractTests
{
    private const string Native = """
        {"Numbers":{"SYMSTG.BNLOT":{"Mode":"RANGE","Val":0.02,"Min":0.01,"Max":0.1}},"Signals":{},"Trends":{"SigWarm.BuyTrd":{"Mode":"STRAT","Val":null,"Min":null,"Max":"NU"}},"Confidence":{},"Methods":{"BAMODE":{"Mode":"STRAT","Val":"WDCA","Opts":["WDCA","GDCA"]}},"NewsFilter":{"Mode":"FIXED","Val":false}}
        """;

    [Fact]
    public void NativeValuesRetainOwnershipNullableStepsAndBooleanChoices()
    {
        var values = JsonSerializer.Deserialize<DtoAIBotValues>(Native, FinHttpTransport.ReadOptions)!;
        Assert.Equal(BotValMode.RANGE, values.Numbers!["SYMSTG.BNLOT"].Mode);
        Assert.Equal(0.01, values.Numbers["SYMSTG.BNLOT"].Min);
        Assert.Null(values.Trends!["SigWarm.BuyTrd"].Val);
        Assert.Null(values.Trends["SigWarm.BuyTrd"].Min);
        Assert.Equal(Trend.NU, values.Trends["SigWarm.BuyTrd"].Max);
        Assert.Equal(AMode.WDCA, values.Methods!["BAMODE"].Val);
        Assert.False(values.NewsFilter!.Val);
        var roundTrip = JsonSerializer.Deserialize<DtoAIBotValues>(JsonSerializer.Serialize(values, FinHttpTransport.WriteOptions), FinHttpTransport.ReadOptions)!;
        Assert.Equal(values.Methods["BAMODE"].Opts, roundTrip.Methods!["BAMODE"].Opts);
        Assert.Equal(BotValMode.STRAT, roundTrip.Trends!["SigWarm.BuyTrd"].Mode);
    }

    [Fact]
    public void EarlyTrailCloseAndDynamicMartingaleReadAsTheirOwnMembers()
    {
        var order = JsonSerializer.Deserialize<DtoFxsOrdr>("""{"OID":9,"CRSN":"ETRAIL"}""", FinHttpTransport.ReadOptions)!;
        Assert.Equal(FxsCloseReason.ETRAIL, order.CRSN);
        var symbol = JsonSerializer.Deserialize<DtoEASymStg>("""{"BAMODE":"DYNM","SAMODE":"DYNM","BALLOWED":["FIRR","DYNM"]}""", FinHttpTransport.ReadOptions)!;
        Assert.Equal(AMode.DYNM, symbol.BAMODE);
        Assert.Equal(AMode.DYNM, symbol.SAMODE);
        Assert.Equal([AMode.FIRR, AMode.DYNM], symbol.BALLOWED);
        Assert.Contains("\"BAMODE\":\"DYNM\"", JsonSerializer.Serialize(symbol, FinHttpTransport.WriteOptions));
    }

    [Fact]
    public void AccountAndPairPatchesCarryOrdinaryValueEdits()
    {
        var values = JsonSerializer.Deserialize<DtoAIBotValues>(Native, FinHttpTransport.ReadOptions)!;
        var account = new CmdApiFxsAcctPatch { Values = values };
        var pair = new CmdApiFxsPairPatch { Values = values };
        Assert.Contains("\"Values\":", JsonSerializer.Serialize(account, FinHttpTransport.WriteOptions));
        Assert.Contains("\"Values\":", JsonSerializer.Serialize(pair, FinHttpTransport.WriteOptions));
        Assert.Equal(values.Numbers["SYMSTG.BNLOT"].Max,
            JsonSerializer.Deserialize<CmdApiFxsAcctPatch>(JsonSerializer.Serialize(account, FinHttpTransport.WriteOptions), FinHttpTransport.ReadOptions)!.Values!.Numbers!["SYMSTG.BNLOT"].Max);
    }
}
