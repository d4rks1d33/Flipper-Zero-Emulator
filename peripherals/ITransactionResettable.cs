//
// Shared marker interface for SPI peripherals that need to drop transient
// per-transfer state when a new SPI transaction begins (CS freshly asserted).
//
// It lives in its own file so it is compiled/loaded before both Spi2Router.cs
// and FlipperSdCard.cs. Because each `i @file.cs` in Renode compiles a separate
// assembly, a shared type must be loaded on its own first for both sides to bind
// to the same interface.
//
namespace Antmicro.Renode.Peripherals.SPI
{
    public interface ITransactionResettable
    {
        // Called when CS is asserted (a new transaction starts) so the peripheral
        // can discard leftover output/state from an already-completed transfer.
        void BeginTransaction();
    }
}
