using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;

namespace CryptoCrush.Models.Contract;

[Struct("User")]
public class User
{
    [Parameter("address", "userAddress", 1)]
    public string UserAddress { get; set; } = string.Empty;
    
    [Parameter("uint256", "amount", 2)]
    public BigInteger Amount { get; set; }
}