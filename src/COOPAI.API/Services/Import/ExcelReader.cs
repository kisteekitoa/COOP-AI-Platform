// ==========================================================
// COOP-AI
// File        : ExcelReader.cs
// Module      : Loan Import
// Version     : 0.1.001
// Description : Read Excel File
// ==========================================================

using System.Data;
using ExcelDataReader;

namespace COOPAI.API.Services.Import;

public class ExcelReader
{
    public DataTable Read(string filePath)
    {
        System.Text.Encoding.RegisterProvider(
            System.Text.CodePagesEncodingProvider.Instance);

        using var stream = File.Open(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        using var reader = ExcelReaderFactory.CreateReader(stream);

        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false
            }
        });

        return dataSet.Tables[0];
    }
}