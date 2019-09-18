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
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using log4net;
using OpenMetaverse;
using Universe.Framework;
using Npgsql;
using NpgsqlTypes;


namespace Universe.Data.PGSQL
{
    /// <summary>
    /// A PGSQL Interface for Avatar Storage
    /// </summary>
    public class PGSQLAvatarData : PGSQLGenericTableHandler<AvatarBaseData>,
            IAvatarData
    {
//        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public PGSQLAvatarData(string connectionString, string realm) :
                base(connectionString, realm, "Avatar")
        {
        }

        public bool Delete(UUID principalID, string name)
        {
            using (NpgsqlConnection conn = new NpgsqlConnection(m_ConnectionString))
            using (NpgsqlCommand cmd = new NpgsqlCommand())
            {

                cmd.CommandText = String.Format("DELETE FROM {0} where \"PrincipalID\" = :PrincipalID and \"Name\" = :Name", m_Realm);
                cmd.Parameters.Add(m_database.CreateParameter("PrincipalID", principalID));
                cmd.Parameters.Add(m_database.CreateParameter("Name", name));
                cmd.Connection = conn;
                conn.Open();
                if (cmd.ExecuteNonQuery() > 0)
                    return true;

                return false;
            }
        }
    }
}