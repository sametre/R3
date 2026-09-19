using System.Runtime.CompilerServices;

// Lets tests exercise the *WithinTransaction composability seams directly (e.g. proving a mid-transaction
// failure rolls back everything, without needing to force that failure through a public entry point like
// LocalSalesService.Post) - see PostingEngineTests.cs.
[assembly: InternalsVisibleTo("R3.Domain.Tests")]
