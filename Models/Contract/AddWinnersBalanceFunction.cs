using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace CryptoCrush.Models.Contract;

[Function("addWinnersBalance")]
public class AddWinnersBalanceFunction : FunctionMessage
{
    [Parameter("tuple[]", "banch",1)]
    public List<User>? Banch { get; set; }
}