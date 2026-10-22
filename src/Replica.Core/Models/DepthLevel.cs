namespace Replica.Core.Models;

/// <summary>盘口深度档位：价格 + 挂单量。</summary>
public readonly record struct DepthLevel(double Price, int Volume);
