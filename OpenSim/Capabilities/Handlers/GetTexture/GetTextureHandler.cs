/* 6 March 2019
 * 
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
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
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.IO;
using System.Threading;
using log4net;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;

namespace OpenSim.Capabilities.Handlers
{
    public class GetTextureHandler
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IAssetService m_assetService;

        public const string DefaultFormat = "x-j2c";

        static private int convertFlag = 0;
        static private readonly ExpiringCache<string, byte[]> ConvertCache = new ExpiringCache<string, byte[]>();

        public GetTextureHandler(IAssetService assService)
        {
            m_assetService = assService;
        }

        public Hashtable Handle(Hashtable request)
        {
            Hashtable ret = new Hashtable();
            ret["int_response_code"] = (int)System.Net.HttpStatusCode.NotFound;
            ret["content_type"] = "text/plain";
            ret["keepalive"] = false; // Seems to always be false
            ret["reusecontext"] = false; // Seems to always be false
            ret["int_bytes"] = 0;
            string textureStr = (string)request["texture_id"];
            string format = (string)request["format"];

            if (m_assetService == null)
            {
                m_log.Error("[GETTEXTURE]: Cannot fetch texture " + textureStr + " without an asset service");
            }

            UUID textureID;
            if (!String.IsNullOrEmpty(textureStr) && UUID.TryParse(textureStr, out textureID))
            {
                string[] formats;
                if (!string.IsNullOrEmpty(format))
                {
                    formats = new string[1] { format.ToLower() };
                }
                else
                {
                    formats = new string[1] { DefaultFormat }; // default
                    if (((Hashtable)request["headers"])["Accept"] != null)
                        formats = WebUtil.GetPreferredImageTypes((string)((Hashtable)request["headers"])["Accept"]);
                    if (formats.Length == 0)
                        formats = new string[1] { DefaultFormat }; // default

                }
                // OK, we have an array with preferred formats, possibly with only one entry
                foreach (string f in formats)
                {
                    if (FetchTexture(request, ret, textureID, f))
                        return ret; // Got it! Done
                }
                // If we arrived here then we did not find the texture
                ret["int_response_code"] = 404;
                ret["error_status_text"] = "not found";
                ret["str_response_string"] = "not found";
            }
            else
            {
                m_log.Warn("[GETTEXTURE]: Failed to parse a texture_id from GetTexture request: " + (string)request["uri"]);
            }
            return ret;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="httpRequest"></param>
        /// <param name="httpResponse"></param>
        /// <param name="textureID"></param>
        /// <param name="format"></param>
        /// <returns>False for "caller try another codec"; true otherwise</returns>
        private bool FetchTexture(Hashtable request, Hashtable response, UUID textureID, string format)
        {
            AssetBase texture = null;
            
            if (format != DefaultFormat)
            {
                string fullID = textureID.ToString() + "-" + format;

                // Try the cache for the non default format. 
                // Get() will also try the cache. So no need doing it 2x for default format.
                texture = m_assetService.GetCached(fullID);
            }

            if (texture == null)
            {
                // Fetch locally or remotely. Misses return a 404
                // Get() will also check the local cache.
                texture = m_assetService.Get(textureID.ToString());

                if (texture != null)
                {
                    if (texture.Type != (sbyte)AssetType.Texture)
                        return true;

                    if (format == DefaultFormat)
                    {
                        WriteTextureData(request, response, texture, format);
                        return true;
                    }
                    else
                    {
                        AssetBase newTexture = new AssetBase(texture.ID + "-" + format, texture.Name, (sbyte)AssetType.Texture, texture.Metadata.CreatorID);
                        newTexture.Data = ConvertTextureData(texture, format);
                        if (newTexture.Data.Length == 0)
                            return false; // !!! Caller try another codec, please!

                        newTexture.Flags = AssetFlags.Collectable;
                        newTexture.Temporary = true;
                        newTexture.Local = true;
                        m_assetService.Store(newTexture);
                        WriteTextureData(request, response, newTexture, format);
                        return true;
                    }
                }
           }
           else // it was in the cache
           {
               WriteTextureData(request, response, texture, format);
               return true;
           }
            return false;
        }

        private void WriteTextureData(Hashtable request, Hashtable response, AssetBase texture, string format)
        {
            Hashtable headers = new Hashtable();
            response["headers"] = headers;

            string range = String.Empty;

            if (((Hashtable)request["headers"])["range"] != null)
                range = (string)((Hashtable)request["headers"])["range"];

            else if (((Hashtable)request["headers"])["Range"] != null)
                range = (string)((Hashtable)request["headers"])["Range"];

            if (!String.IsNullOrEmpty(range)) // JP2's only
            {
                // Range request
                int start, end;
                if (TryParseRange(range, out start, out end))
                {
                    // Before clamping start make sure we can satisfy it in order to avoid
                    // sending back the last byte instead of an error status
                    if (start >= texture.Data.Length)
                    {
                        // Stricly speaking, as per http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html, we should be sending back
                        // Requested Range Not Satisfiable (416) here.  However, it appears that at least recent implementations
                        // of the Linden Lab viewer (3.2.1 and 3.3.4 and probably earlier), a viewer that has previously
                        // received a very small texture  may attempt to fetch bytes from the server past the
                        // range of data that it received originally.  Whether this happens appears to depend on whether
                        // the viewer's estimation of how large a request it needs to make for certain discard levels
                        // (http://wiki.secondlife.com/wiki/Image_System#Discard_Level_and_Mip_Mapping), chiefly discard
                        // level 2.  If this estimate is greater than the total texture size, returning a RequestedRangeNotSatisfiable
                        // here will cause the viewer to treat the texture as bad and never display the full resolution
                        // However, if we return PartialContent (or OK) instead, the viewer will display that resolution.

//                        response.StatusCode = (int)System.Net.HttpStatusCode.RequestedRangeNotSatisfiable;
                        // viewers don't seem to handle RequestedRangeNotSatisfiable and keep retrying with same parameters
                        response["int_response_code"] = (int)System.Net.HttpStatusCode.NotFound;
                    }
                    else
                    {
                        // Handle the case where no second range value was given.  This is equivalent to requesting
                        // the rest of the entity.
                        if (end == -1)
                            end = int.MaxValue;

                        end = Utils.Clamp(end, 0, texture.Data.Length - 1);
                        start = Utils.Clamp(start, 0, end);
                        int len = end - start + 1;

                        response["content-type"] = texture.Metadata.ContentType;

                        if (start == 0 && len == texture.Data.Length) // well redudante maybe
                        {
                            response["int_response_code"] = (int)System.Net.HttpStatusCode.OK;
                            response["bin_response_data"] = texture.Data;
                            response["int_bytes"] = texture.Data.Length;
                        }
                        else
                        {
                            response["int_response_code"] = (int)System.Net.HttpStatusCode.PartialContent;
                            headers["Content-Range"] = String.Format("bytes {0}-{1}/{2}", start, end, texture.Data.Length);

                            byte[] d = new byte[len];
                            Array.Copy(texture.Data, start, d, 0, len);
                            response["bin_response_data"] = d;
                            response["int_bytes"] = len;
                        }
                    }
                }
                else
                {
                    m_log.Warn("[GETTEXTURE]: Malformed Range header: " + range);
                    response["int_response_code"] = (int)System.Net.HttpStatusCode.BadRequest;
                }
            }
            else // JP2's or other formats
            {
                // Full content request
                response["int_response_code"] = (int)System.Net.HttpStatusCode.OK;
                if (format == DefaultFormat)
                    response["content_type"] = texture.Metadata.ContentType;
                else
                    response["content_type"] = "image/" + format;

                response["bin_response_data"] = texture.Data;
                response["int_bytes"] = texture.Data.Length;
            }
        }

        /// <summary>
        /// Parse a range header.
        /// </summary>
        /// <remarks>
        /// As per http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html,
        /// this obeys range headers with two values (e.g. 533-4165) and no second value (e.g. 533-).
        /// Where there is no value, -1 is returned.
        /// FIXME: Need to cover the case where only a second value is specified (e.g. -4165), probably by returning -1
        /// for start.</remarks>
        /// <returns></returns>
        /// <param name='header'></param>
        /// <param name='start'>Start of the range.  Undefined if this was not a number.</param>
        /// <param name='end'>End of the range.  Will be -1 if no end specified.  Undefined if there was a raw string but this was not a number.</param>
        private bool TryParseRange(string header, out int start, out int end)
        {
            start = end = 0;

            if (header.StartsWith("bytes="))
            {
                string[] rangeValues = header.Substring(6).Split('-');

                if (rangeValues.Length == 2)
                {
                    if (!Int32.TryParse(rangeValues[0], out start))
                        return false;

                    string rawEnd = rangeValues[1];

                    if (rawEnd == "")
                    {
                        end = -1;
                        return true;
                    }
                    else if (Int32.TryParse(rawEnd, out end))
                    {
                        return true;
                    }
                }
            }
            start = end = 0;
            return false;
        }

        private byte[] ConvertTextureData(AssetBase texture, string format)
        {
            byte[] data = new byte[0];

			ImageCodecInfo codec = GetEncoderInfo("image/" + format);
			if (codec == null)
			{
				m_log.WarnFormat("[GETTEXTURE]: No such codec {0}", format);
				return data;
			}

            string cacheID = texture.ID + "-" + format;

            // Each time we use this texture we update the cache.
            // Claim the flag!
            // Set the flag to 1 only if it is 0 and return its old value.
            // So if it is already 1 it will return 1 and not change it.
            if (0 == Interlocked.CompareExchange(ref convertFlag, 1, 0))
            {
                try
                {
                    if (ConvertCache.TryGetValue(cacheID, out data))
                    {
                        ConvertCache.AddOrUpdate(cacheID, data, 300);
                        return data;
                    }
                }
                finally
                {
                    // Release flag.
                    Interlocked.Exchange(ref convertFlag, 0);
                }
            }

			ManagedImage managedImage = null;
            try
            {
				// Taking our jpeg2000 data, decoding it, then saving it to a byte array with regular data

				// Decode image to System.Drawing.Image
				// if (OpenJPEG.DecodeToImage(texture.Data, out managedImage, out image) && image != null)
				if (OpenJPEG.DecodeToImage(texture.Data, out managedImage) && managedImage != null)
				{
					// Save to bitmap
					using (Bitmap mTexture = managedImage.ExportBitmap())
					{
						using (EncoderParameters myEncoderParameters = new EncoderParameters())
						{
							myEncoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 95L);

							// Save bitmap to stream
							using (MemoryStream imgstream = new MemoryStream())
							{
								mTexture.Save(imgstream, codec, myEncoderParameters);
								// Write the stream to a byte array for output
								data = imgstream.ToArray();
                            }

                        }
                    }

                    // Claim the flag!
                    // Set the flag to 1 only if it is 0 and return its old value.
                    // So if it is already 1 it will return 1 and not change it.
                    if (0 == Interlocked.CompareExchange(ref convertFlag, 1, 0))
                    {
                        try
                        {
                            ConvertCache.AddOrUpdate(cacheID, data, 300);
                        }
                        finally
                        {
                            // Release flag.
                            Interlocked.Exchange(ref convertFlag, 0);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[GETTEXTURE]: Unable to convert texture {0} to {1}: {2}", texture.ID, format, e.Message);
            }
            finally
            {
                // Reclaim memory, these are unmanaged resources
                // If we encountered an exception, one or more of these will be null
                try
                {
                    if (managedImage != null)
                        managedImage.Clear();
                }
                catch { }
            }

            return data;
        }

        private static readonly ImageCodecInfo[] m_encoders = ImageCodecInfo.GetImageEncoders();

        // From msdn
        private static ImageCodecInfo GetEncoderInfo(String mimeType)
        {
            for (int j = 0; j < m_encoders.Length; ++j)
            {
                if (m_encoders[j].MimeType == mimeType)
                    return m_encoders[j];
            }
            return null;
        }
    }
}
