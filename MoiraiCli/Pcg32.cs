namespace Pcg;
internal static class Helpers
{
	/// <summary>
	/// Implements <c>pcg_advance_lcg_64</c>.
	/// </summary>
	/// <remarks>See <a href="https://github.com/imneme/pcg-c/blob/e2383c4bfcc862b40c3d85a43c9d495ff61186cb/src/pcg-advance-64.c#L46">original source</a>.</remarks>
	public static ulong Advance(ulong state, ulong delta, ulong multiplier, ulong increment)
	{
		// corresponds to pcg_advance_lcg_64
		ulong accumulatedMultipler = 1u;
		ulong accumulatedIncrement = 0u;
		while (delta > 0)
		{
			if (delta % 2 == 1)
			{
				accumulatedMultipler *= multiplier;
				accumulatedIncrement = accumulatedIncrement * multiplier + increment;
			}
			increment = (multiplier + 1) * increment;
			multiplier *= multiplier;
			delta /= 2;
		}
		return accumulatedMultipler * state + accumulatedIncrement;
	}

	/// <summary>
	/// Implements <c>pcg_output_xsh_rs_64_32</c>.
	/// </summary>
	/// <remarks>See <a href="https://github.com/imneme/pcg-c/blob/e2383c4bfcc862b40c3d85a43c9d495ff61186cb/include/pcg_variants.h#L133">original source</a>.</remarks>
	public static uint OutputXshRs(ulong state) =>
		unchecked((uint) (((state >> 22) ^ state) >> (int) ((state >> 61) + 22u)));

	/// <summary>
	/// Implements <c>pcg_output_xsh_rr_64_32</c>.
	/// </summary>
	/// <remarks>See <a href="https://github.com/imneme/pcg-c/blob/e2383c4bfcc862b40c3d85a43c9d495ff61186cb/include/pcg_variants.h#L158">original source</a>.</remarks>
	public static uint OutputXshRr(ulong state) =>
		RotateRight((uint) (((state >> 18) ^ state) >> 27), (int) (state >> 59));

	/// <summary>
	/// Implements <c>pcg_rotr_32</c>.
	/// </summary>
	/// <param name="value">The value to rotate right.</param>
	/// <param name="rotate">The number of bits to rotate.</param>
	/// <returns>The input <paramref name="value"/>, rotated right by <paramref name="rotate"/> bits.</returns>
	/// <remarks>See <a href="https://github.com/imneme/pcg-c/blob/e2383c4bfcc862b40c3d85a43c9d495ff61186cb/include/pcg_variants.h#L88">original source</a>.</remarks>
	public static uint RotateRight(uint value, int rotate) =>
		(value >> rotate) | (value << (-rotate & 31));

	/// <summary>
	/// Represents <c>PCG_DEFAULT_MULTIPLIER_64</c>.
	/// </summary>
	/// <remarks>See <a href="https://github.com/imneme/pcg-c/blob/e2383c4bfcc862b40c3d85a43c9d495ff61186cb/include/pcg_variants.h#L253">original source</a>.</remarks>
	public const ulong Multiplier64 = 6364136223846793005ul;

	/// <summary>
	/// Represents <c>PCG_DEFAULT_INCREMENT_64</c>.
	/// </summary>
	/// <remarks>See <a href="https://github.com/imneme/pcg-c/blob/e2383c4bfcc862b40c3d85a43c9d495ff61186cb/include/pcg_variants.h#L258">original source</a>.</remarks>
	public const ulong Increment64 = 1442695040888963407ul;
}
/// <summary>
/// Implements the <code>pcg32</code> random number generator described
/// at <a href="http://www.pcg-random.org/using-pcg-c.html">http://www.pcg-random.org/using-pcg-c.html</a>.
/// </summary>
public sealed class Pcg32
{
	/// <summary>
	/// Initializes a new instance of the <see cref="Pcg32"/> pseudorandom number generator.
	/// </summary>
	/// <param name="state">The starting state for the RNG; you can pass any 64-bit value.</param>
	/// <param name="sequence">The output sequence for the RNG; you can pass any 64-bit value, although only the low
	/// 63 bits are significant.</param>
	/// <remarks>For this generator, there are 2<sup>63</sup> possible sequences of pseudorandom numbers. Each sequence
	/// is entirely distinct and has a period of 2<sup>64</sup>. The <paramref name="sequence"/> argument selects which
	/// stream you will use. The <paramref name="state"/> argument specifies where you are in that 2<sup>64</sup> period.</remarks>
	public Pcg32(ulong state, ulong sequence)
	{
		// implements pcg_setseq_64_srandom_r
		_inc = (sequence << 1) | 1u;
		Step();
		_state += state;
		Step();
	}

	/// <summary>
	/// Generates the next random number.
	/// </summary>
	/// <returns></returns>
	public uint GenerateNext()
	{
		// implements pcg_setseq_64_xsh_rr_32_random_r
		var oldState = _state;
		Step();
		return Helpers.OutputXshRr(oldState);
	}
	public float GenerateNextFloat()
	{
		// implements pcg_setseq_64_xsh_rr_32_random_r
		uint next = GenerateNext();
		return next / (float)uint.MaxValue;
	}

	/// <summary>
	/// Generates a uniformly distributed 32-bit unsigned integer less than <paramref name="bound"/> (i.e., <c>x</c> where
	/// <c>0 &lt;= x &lt; bound</c>.
	/// </summary>
	/// <param name="bound">The exclusive upper bound of the random number to be generated.</param>
	/// <returns>A random number between <c>0</c> and <paramref name="bound"/> (exclusive).</returns>
	public uint GenerateNext(uint bound)
	{
		// implements pcg_setseq_64_xsh_rr_32_boundedrand_r
		uint threshold = ((uint) -bound) % bound;
		while (true)
		{
			uint r = GenerateNext();
			if (r >= threshold)
				return r % bound;
		}
	}

	/// <summary>
	/// Advances the RNG by <paramref name="delta"/> steps, doing so in <c>log(delta)</c> time.
	/// </summary>
	/// <param name="delta">The number of steps to advance; pass <c>2<sup>64</sup> - delta</c> (i.e., <c>-delta</c>) to go backwards.</param>
	public void Advance(ulong delta)
	{
		// implements pcg_setseq_64_advance_r
		_state = Helpers.Advance(_state, delta, Helpers.Multiplier64, _inc);
	}
	
	private void Step()
	{
		// corresponds to pcg_setseq_64_step_r
		_state = unchecked(_state * Helpers.Multiplier64 + _inc);
	}

	readonly ulong _inc;
	ulong _state;
}