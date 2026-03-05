// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DotNet.Interactive.Bash;

/// <summary>
/// Provides extension methods for configuring bash kernel support.
/// </summary>
public static class BashKernelExtensions
{
    /// <summary>
    /// Adds a bash kernel to the composite kernel.
    /// </summary>
    /// <param name="kernel">The composite kernel to add the bash kernel to.</param>
    /// <param name="options">Optional configuration options for the bash kernel.</param>
    /// <returns>The composite kernel with the bash kernel added.</returns>
    /// <exception cref="BashNotAvailableException">
    /// Thrown when bash is not available on the system.
    /// </exception>
    public static CompositeKernel UseBash(
        this CompositeKernel kernel,
        BashKernelOptions? options = null)
    {
        var bashKernel = new BashKernel(BashKernel.DefaultKernelName, options);
        kernel.Add(bashKernel, ["bash", "sh"]);
        bashKernel.UseValueSharing();

        return kernel;
    }

    /// <summary>
    /// Adds a bash kernel to the composite kernel with a custom name.
    /// </summary>
    /// <param name="kernel">The composite kernel to add the bash kernel to.</param>
    /// <param name="name">The name for the bash kernel.</param>
    /// <param name="options">Optional configuration options for the bash kernel.</param>
    /// <returns>The composite kernel with the bash kernel added.</returns>
    /// <exception cref="BashNotAvailableException">
    /// Thrown when bash is not available on the system.
    /// </exception>
    public static CompositeKernel UseBash(
        this CompositeKernel kernel,
        string name,
        BashKernelOptions? options = null)
    {
        var bashKernel = new BashKernel(name, options);
        kernel.Add(bashKernel, ["bash", "sh"]);
        bashKernel.UseValueSharing();

        return kernel;
    }
}
