using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>提供注入的 UTC 值；读取本身不执行工具。</summary>
public interface IClock
{
    /// <summary>提供注入的 UTC 值；读取本身不执行工具。</summary>
    DateTimeOffset UtcNow { get; }
}
