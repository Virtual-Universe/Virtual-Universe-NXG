/*
 * Copyright (c) Contributors, http://virtual-planets.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Universeulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using Universe.Region.Framework.Interfaces;

namespace Universe.Region.CoreModules.World.Terrain
{
    public interface ITerrainModifier
    {
        /// <summary>
        /// Creates the feature.
        /// </summary>
        /// <returns>
        /// Empty string if successful, otherwise error message.
        /// </returns>
        /// <param name='map'>
        /// ITerrainChannel holding terrain data.
        /// </param>
        /// <param name='args'>
        /// command-line arguments from console.
        /// </param>
        string ModifyTerrain(ITerrainChannel map, string[] args);

        /// <summary>
        /// Gets a string describing the usage.
        /// </summary>
        /// <returns>
        /// A string describing parameters for creating the feature.
        /// Format is "feature-name <arg1> <arg2> ..."
        /// </returns>
        string GetUsage();

        /// <summary>
        /// Apply the appropriate operation on the specified map, at (x, y).
        /// </summary>
        /// <param name='map'>
        /// Map.
        /// </param>
        /// <param name='data'>
        /// Data.
        /// </param>
        /// <param name='x'>
        /// X.
        /// </param>
        /// <param name='y'>
        /// Y.
        /// </param>
        double operate(double[,] map, TerrainModifierData data, int x, int y);
    }

}
