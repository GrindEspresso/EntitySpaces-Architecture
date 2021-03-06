/*  New BSD License
-------------------------------------------------------------------------------
Copyright (c) 2006-2012, EntitySpaces, LLC
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.
    * Neither the name of the EntitySpaces, LLC nor the
      names of its contributors may be used to endorse or promote products
      derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL EntitySpaces, LLC BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
-------------------------------------------------------------------------------
*/

using System;
using System.Collections.Generic;
using System.Data;

using EntitySpaces.DynamicQuery;
using EntitySpaces.Interfaces;

using iAnywhere.Data.SQLAnywhere;

namespace EntitySpaces.SybaseSqlAnywhereProvider
{
    class Shared
    {
        static public SACommand BuildDynamicInsertCommand(esDataRequest request, List<string> modifiedColumns)
        {
            string sql = String.Empty;
            string defaults = String.Empty;
            string into = String.Empty;
            string values = String.Empty;
            string comma = String.Empty;
            string defaultComma = String.Empty;
            string where = String.Empty;
            string whereComma = String.Empty;

            PropertyCollection props = new PropertyCollection();

            Dictionary<string, SAParameter> types = Cache.GetParameters(request);

            SACommand cmd = new SACommand();
            if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;

            esColumnMetadataCollection cols = request.Columns;
            foreach (esColumnMetadata col in cols)
            {
                bool isModified = modifiedColumns == null ? false : modifiedColumns.Contains(col.Name);

                if (isModified && (!col.IsAutoIncrement && !col.IsConcurrency && !col.IsEntitySpacesConcurrency))
                {
                    SAParameter p = types[col.Name];
                    cmd.Parameters.Add(CloneParameter(p));

                    into += comma + Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose;
                    values += comma + p.ParameterName;
                    comma = ", ";
                }
                else if (col.IsAutoIncrement)
                {
                    props["AutoInc"] = col.Name;
                    props["Source"] = request.ProviderMetadata.Source;
                }
                else if (col.IsConcurrency)
                {
                    props["Timestamp"] = col.Name;
                    props["Source"] = request.ProviderMetadata.Source;
                }
                else if (col.IsEntitySpacesConcurrency)
                {
                    props["EntitySpacesConcurrency"] = col.Name;

                    SAParameter p = types[col.Name];

                    into += comma + Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose;
                    values += comma + "1";
                    comma = ", ";

                    SAParameter clone = CloneParameter(p);
                    clone.Value = 1;
                    cmd.Parameters.Add(clone);
                }
                else if (col.IsComputed)
                {
                    // Do nothing but leave this here
                }
                else if (cols.IsSpecialColumn(col))
                {
                    // Do nothing but leave this here
                }
                else if (col.HasDefault)
                {
                    defaults += defaultComma + Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose;
                    defaultComma = ",";
                }

                if (col.IsInPrimaryKey)
                {
                    where += whereComma + col.Name;
                    whereComma = ",";
                }
            }

            #region Special Columns
            if (cols.DateAdded != null && cols.DateAdded.IsServerSide)
            {
                into += comma + Delimiters.ColumnOpen + cols.DateAdded.ColumnName + Delimiters.ColumnClose;
                values += comma + request.ProviderMetadata["DateAdded.ServerSideText"];
                comma = ", ";

                defaults += defaultComma + Delimiters.ColumnOpen + cols.DateAdded.ColumnName + Delimiters.ColumnClose;
                defaultComma = ",";
            }

            if (cols.DateModified != null && cols.DateModified.IsServerSide)
            {
                into += comma + Delimiters.ColumnOpen + cols.DateModified.ColumnName + Delimiters.ColumnClose;
                values += comma + request.ProviderMetadata["DateModified.ServerSideText"];
                comma = ", ";

                defaults += defaultComma + Delimiters.ColumnOpen + cols.DateModified.ColumnName + Delimiters.ColumnClose;
                defaultComma = ",";
            }

            if (cols.AddedBy != null && cols.AddedBy.IsServerSide)
            {
                into += comma + Delimiters.ColumnOpen + cols.AddedBy.ColumnName + Delimiters.ColumnClose;
                values += comma + request.ProviderMetadata["AddedBy.ServerSideText"];
                comma = ", ";

                defaults += defaultComma + Delimiters.ColumnOpen + cols.AddedBy.ColumnName + Delimiters.ColumnClose;
                defaultComma = ",";
            }

            if (cols.ModifiedBy != null && cols.ModifiedBy.IsServerSide)
            {
                into += comma + Delimiters.ColumnOpen + cols.ModifiedBy.ColumnName + Delimiters.ColumnClose;
                values += comma + request.ProviderMetadata["ModifiedBy.ServerSideText"];
                comma = ", ";

                defaults += defaultComma + Delimiters.ColumnOpen + cols.ModifiedBy.ColumnName + Delimiters.ColumnClose;
                defaultComma = ",";
            }
            #endregion

            if (defaults.Length > 0)
            {
                comma = String.Empty;
                props["Defaults"] = defaults;
                props["Where"] = where;
            }

            sql += " INSERT INTO " + CreateFullName(request);

            if (into.Length != 0)
            {
                sql += "(" + into + ") VALUES (" + values + ")";
            }
            else
            {
                sql += "DEFAULT VALUES";
            }

            request.Properties = props;

            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            return cmd;
        }

        static public SACommand BuildDynamicUpdateCommand(esDataRequest request, List<string> modifiedColumns)
        {
            string where = String.Empty;
            string scomma = String.Empty;
            string wcomma = String.Empty;
            string defaults = String.Empty;
            string defaultsWhere = String.Empty;
            string defaultsComma = String.Empty;
            string defaultsWhereComma = String.Empty;
            List<SAParameter> whereParams = new List<SAParameter>();

            string sql = "UPDATE " + CreateFullName(request) + " SET ";

            PropertyCollection props = new PropertyCollection();

            Dictionary<string, SAParameter> types = Cache.GetParameters(request);

            SACommand cmd = new SACommand();
            if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;

            esColumnMetadataCollection cols = request.Columns;
            foreach (esColumnMetadata col in cols)
            {
                bool isModified = modifiedColumns == null ? false : modifiedColumns.Contains(col.Name);

                if (isModified && (!col.IsAutoIncrement && !col.IsConcurrency && !col.IsEntitySpacesConcurrency))
                {
                    SAParameter p = CloneParameter(types[col.Name]);
                    cmd.Parameters.Add(p);

                    sql += scomma;
                    sql += Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose + " = " + p.ParameterName;
                    scomma = ", ";
                }
                else if (col.IsAutoIncrement)
                {
                    // Nothing to do but leave this here
                }
                else if (col.IsConcurrency)
                {
                    SAParameter p = CloneParameter(types[col.Name]);
                    p.SourceVersion = DataRowVersion.Original;
                    //cmd.Parameters.Add(p);
                    whereParams.Add(p);

                    where += wcomma;
                    where += Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose + " = " + p.ParameterName;
                    wcomma = " AND ";
                }
                else if (col.IsEntitySpacesConcurrency)
                {
                    props["EntitySpacesConcurrency"] = col.Name;

                    SAParameter p = CloneParameter(types[col.Name]);
                    p.SourceVersion = DataRowVersion.Original;
                    //cmd.Parameters.Add(p);
                    whereParams.Add(p);


                    sql += scomma;
                    sql += Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose + " = " + Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose + " + 1";
                    scomma = ", ";

                    where += wcomma;
                    where += Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose + " = " + p.ParameterName;
                    wcomma = " AND ";
                }
                else if (col.IsComputed)
                {
                    // Do nothing but leave this here
                }
                else if (cols.IsSpecialColumn(col))
                {
                    // Do nothing but leave this here
                }
                else if (col.HasDefault)
                {
                    // defaults += defaultsComma + Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose;
                    // defaultsComma = ",";
                }

                if (col.IsInPrimaryKey)
                {
                    SAParameter p = CloneParameter(types[col.Name]);
                    p.SourceVersion = DataRowVersion.Original;
                    //cmd.Parameters.Add(p);
                    whereParams.Add(p);

                    where += wcomma;
                    where += Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose + " = " + p.ParameterName;
                    wcomma = " AND ";

                    defaultsWhere += defaultsWhereComma + col.Name;
                    defaultsWhereComma = ",";
                }
            }

            #region Special Columns
            if (cols.DateModified != null && cols.DateModified.IsServerSide)
            {
                sql += scomma;
                sql += Delimiters.ColumnOpen + cols.DateModified.ColumnName + Delimiters.ColumnClose + " = " + request.ProviderMetadata["DateModified.ServerSideText"];
                scomma = ", ";

                defaults += defaultsComma + Delimiters.ColumnOpen + cols.DateModified.ColumnName + Delimiters.ColumnClose;
                defaultsComma = ",";
            }

            if (cols.ModifiedBy != null && cols.ModifiedBy.IsServerSide)
            {
                sql += scomma;
                sql += Delimiters.ColumnOpen + cols.ModifiedBy.ColumnName + Delimiters.ColumnClose + " = " + request.ProviderMetadata["ModifiedBy.ServerSideText"];
                scomma = ", ";

                defaults += defaultsComma + Delimiters.ColumnOpen + cols.ModifiedBy.ColumnName + Delimiters.ColumnClose;
                defaultsComma = ",";
            }
            #endregion

            if (defaults.Length > 0)
            {
                props["Defaults"] = defaults;
                props["Where"] = defaultsWhere;
            }

            if (whereParams.Count > 0)
            {
                foreach (SAParameter p in whereParams)
                {
                    cmd.Parameters.Add(p);
                }
            }

            sql += " WHERE " + where + ";";

            request.Properties = props;

            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            return cmd;
        }

        static public SACommand BuildDynamicDeleteCommand(esDataRequest request, esEntitySavePacket packet)
        {
            Dictionary<string, SAParameter> types = Cache.GetParameters(request);

            SACommand cmd = new SACommand();
            if (request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;

            string sql = "DELETE FROM " + CreateFullName(request) + " ";

            string comma = String.Empty;
            comma = String.Empty;
            sql += " WHERE ";
            foreach (esColumnMetadata col in request.Columns)
            {
                if (col.IsInPrimaryKey || col.IsEntitySpacesConcurrency)
                {
                    SAParameter p = types[col.Name];
                    cmd.Parameters.Add(CloneParameter(p));

                    sql += comma;
                    sql += Delimiters.ColumnOpen + col.Name + Delimiters.ColumnClose + " = " + p.ParameterName;
                    comma = " AND ";
                }
            }

            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            return cmd;
        }

        static public SACommand BuildStoredProcInsertCommand(esDataRequest request)
        {
            Dictionary<string, SAParameter> types = Cache.GetParameters(request);

            SACommand cmd = new SACommand();
            if(request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;

            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = CreateFullSPName(request, request.ProviderMetadata.spInsert);

            PopulateStoredProcParameters(cmd, request);

            foreach (esColumnMetadata col in request.Columns)
            {
                if (col.HasDefault &&
                    (col.Default.ToLower().Contains("newid") || col.Default.ToLower().Contains("newsequentialid")))
                {
                    // They could pre-assign this even though it has a default
                    SAParameter p = types[col.Name];
                    p = cmd.Parameters[p.ParameterName];
                    p.Direction = ParameterDirection.InputOutput;
                }
                if (col.IsComputed || col.IsAutoIncrement || col.IsEntitySpacesConcurrency)
                {
                    SAParameter p = types[col.Name];
                    p = cmd.Parameters[p.ParameterName];
                    p.Direction = ParameterDirection.InputOutput;
                }
            }

            return cmd;
        }

        static public SACommand BuildStoredProcUpdateCommand(esDataRequest request)
        {
            Dictionary<string, SAParameter> types = Cache.GetParameters(request);

            SACommand cmd = new SACommand();
            if(request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;

            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = CreateFullSPName(request, request.ProviderMetadata.spUpdate);

            PopulateStoredProcParameters(cmd, request);

            foreach (esColumnMetadata col in request.Columns)
            {
                if (col.IsComputed || col.IsConcurrency || col.IsEntitySpacesConcurrency)
                {
                    SAParameter p = types[col.Name];
                    p = cmd.Parameters[p.ParameterName];
                    p.Direction = ParameterDirection.InputOutput;
                }
            }

            return cmd;
        }

        static public SACommand BuildStoredProcDeleteCommand(esDataRequest request)
        {
            Dictionary<string, SAParameter> types = Cache.GetParameters(request);

            SACommand cmd = new SACommand();
            if(request.CommandTimeout != null) cmd.CommandTimeout = request.CommandTimeout.Value;

            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = CreateFullSPName(request, request.ProviderMetadata.spDelete);

            SAParameter p;

            foreach (esColumnMetadata col in request.Columns)
            {
                if (col.IsInPrimaryKey)
                {
                    p = types[col.Name];
                    p = CloneParameter(p);
                    p.SourceVersion = DataRowVersion.Current;
                    cmd.Parameters.Add(p);
                }
                else if (col.IsConcurrency || col.IsEntitySpacesConcurrency)
                {
                    p = types[col.Name];
                    p = CloneParameter(p);
                    p.SourceVersion = DataRowVersion.Original;
                    cmd.Parameters.Add(p);
                }
            }

            return cmd;
        }

        static public void PopulateStoredProcParameters(SACommand cmd, esDataRequest request)
        {
            Dictionary<string, SAParameter> types = Cache.GetParameters(request);

            SAParameter p;

            foreach (esColumnMetadata col in request.Columns)
            {
                p = types[col.Name];
                p = CloneParameter(p);
                p.SourceVersion = DataRowVersion.Current;

                if (p.SADbType == SADbType.TimeStamp)
                {
                    p.Direction = ParameterDirection.InputOutput;
                }

                if (col.IsComputed && col.CharacterMaxLength > 0)
                {
                    p.Size = (int)col.CharacterMaxLength;
                }

                cmd.Parameters.Add(p);
            }
        }

        static private SAParameter CloneParameter(SAParameter p)
        {
            ICloneable param = p as ICloneable;
            return param.Clone() as SAParameter;
        }

        static public bool HasUpdates(DataRowCollection rows, out bool insert, out bool update, out bool delete)
        {
            insert = false;
            update = false;
            delete = false;

            for (int i = 0; i < rows.Count; i++)
            {
                switch (rows[i].RowState)
                {
                    case DataRowState.Added:
                        insert = true;
                        break;
                    case DataRowState.Modified:
                        update = true;
                        break;
                    case DataRowState.Deleted:
                        delete = true;
                        break;
                }
            }

            return (insert || update || delete) ? true : false;
        }

        static public bool HasUpdate(DataRow row, out bool insert, out bool update, out bool delete)
        {
            insert = false;
            update = false;
            delete = false;

            switch (row.RowState)
            {
                case DataRowState.Added:
                    insert = true;
                    break;
                case DataRowState.Modified:
                    update = true;
                    break;
                case DataRowState.Deleted:
                    delete = true;
                    break;
            }

            return (insert || update || delete) ? true : false;
        }

        static public string CreateFullName(esDataRequest request, esDynamicQuerySerializable query)
        {
            IDynamicQuerySerializableInternal iQuery = query as IDynamicQuerySerializableInternal;

            esProviderSpecificMetadata providerMetadata = iQuery.ProviderMetadata as esProviderSpecificMetadata;

            string name = String.Empty;

            string catalog = iQuery.Catalog ?? request.Catalog ?? providerMetadata.Catalog;
            string schema = iQuery.Schema ?? request.Schema ?? providerMetadata.Schema;

            if (catalog != null && schema != null)
            {
                name += Delimiters.TableOpen + catalog + Delimiters.TableClose + ".";
            }

            if (schema != null)
            {
                name += Delimiters.TableOpen + schema + Delimiters.TableClose + ".";
            }

            name += Delimiters.TableOpen;
            if (query.es.QuerySource != null)
                name += query.es.QuerySource;
            else
                name += providerMetadata.Destination;
            name += Delimiters.TableClose;

            return name;
        }

        static public string CreateFullName(esDataRequest request)
        {
            string name = String.Empty;

            string catalog = request.Catalog ?? request.ProviderMetadata.Catalog;
            string schema = request.Schema ?? request.ProviderMetadata.Schema;

            if (catalog != null && schema != null)
            {
                name += Delimiters.TableOpen + catalog + Delimiters.TableClose + ".";
            }

            if (schema != null)
            {
                name += Delimiters.TableOpen + schema + Delimiters.TableClose + ".";
            }

            name += Delimiters.TableOpen;
            if(request.DynamicQuery != null && request.DynamicQuery.es.QuerySource != null)
                name += request.DynamicQuery.es.QuerySource;
            else
                name += request.QueryText != null ? request.QueryText : request.ProviderMetadata.Destination;
            name += Delimiters.TableClose;

            return name;
        }

        static public string CreateFullSPName(esDataRequest request, string spName)
        {
            string name = String.Empty;

            if ( (request.Catalog != null || request.ProviderMetadata.Catalog != null) &&
                 (request.Schema != null || request.ProviderMetadata.Schema != null) )
            {
                name += Delimiters.TableOpen;
                name += request.Catalog != null ? request.Catalog : request.ProviderMetadata.Catalog;
                name += Delimiters.TableClose + ".";
            }

            if (request.Schema != null || request.ProviderMetadata.Schema != null)
            {
                name += Delimiters.TableOpen;
                name += request.Schema != null ? request.Schema : request.ProviderMetadata.Schema;
                name += Delimiters.TableClose + ".";
            }

            name += Delimiters.StoredProcNameOpen;
            name += spName;
            name += Delimiters.StoredProcNameClose;

            return name;
        }

        static public esConcurrencyException CheckForConcurrencyException(SAException ex)
        {
            esConcurrencyException ce = null;

            if (ex.Errors != null)
            {
                foreach (SAError err in ex.Errors)
                {
                    if (err.NativeError == 532)
                    {
                        ce = new esConcurrencyException(err.Message, ex);
                        ce.Source = err.Source;
                        break;
                    }
                }
            }

            return ce;
        }

        static public void AddParameters(SACommand cmd, esDataRequest request)
        {
            if (request.QueryType == esQueryType.Text && request.QueryText != null && request.QueryText.Contains("{0}"))
            {
                int i = 0;
                string token = String.Empty;
                string sIndex = String.Empty;
                string param = String.Empty;

                foreach (esParameter esParam in request.Parameters)
                {
                    sIndex = i.ToString();
                    token = '{' + sIndex + '}';
                    param = "?"; // Delimiters.Param + "p" + sIndex;
                    request.QueryText = request.QueryText.Replace(token, param);
                    i++;

                    cmd.Parameters.AddWithValue(Delimiters.Param + esParam.Name, esParam.Value);
                }
            }
            else
            {
                SAParameter param;

                foreach (esParameter esParam in request.Parameters)
                {
                    param = cmd.Parameters.AddWithValue(Delimiters.Param + esParam.Name, esParam.Value);

                    switch (esParam.Direction)
                    {
                        case esParameterDirection.InputOutput:
                            param.Direction = ParameterDirection.InputOutput;
                            break;

                        case esParameterDirection.Output:
                            param.Direction = ParameterDirection.Output;
                            param.DbType = esParam.DbType;
                            param.Size = esParam.Size;
                            param.Scale = esParam.Scale;
                            param.Precision = esParam.Precision;
                            break;

                        case esParameterDirection.ReturnValue:
                            param.Direction = ParameterDirection.ReturnValue;
                            break;

                        // The default is ParameterDirection.Input;
                    }
                }
            }
        }

        static public void GatherReturnParameters(SACommand cmd, esDataRequest request, esDataResponse response)
        {
            if (cmd.Parameters.Count > 0)
            {
                if (request.Parameters != null && request.Parameters.Count > 0)
                {
                    response.Parameters = new esParameters();

                    foreach (esParameter esParam in request.Parameters)
                    {
                        if (esParam.Direction != esParameterDirection.Input)
                        {
                            response.Parameters.Add(esParam);
                            SAParameter p = cmd.Parameters[Delimiters.Param + esParam.Name];
                            esParam.Value = p.Value;
                        }
                    }
                }
            }
        }
    }
}
