﻿Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Globalization

Public Class cFileImportOmicronMatrixCurve
    Inherits cFileImportOmicronMatrix
    Implements iFileImport_SpectroscopyTable

    ''' <summary>
    ''' Columnname of the x-column, when beeing generated by the software.
    ''' </summary>
    Public Const XColumnName_Voltage As String = "voltage"

    ''' <summary>
    ''' Columnname of the x-column, when beeing generated by the software.
    ''' </summary>
    Public Const XColumnName_Length As String = "distance"

    ''' <summary>
    ''' Imports Omicron Matrix spectroscopy files.
    ''' 
    ''' Created for SpectraFox by Michael Ruby.
    ''' 
    ''' We acknowledge the Gwyddion source code
    ''' for the Matrix scan image file import routine,
    ''' which was released under GPL by the following people:
    ''' 
    ''' Copyright (C) 2008, Philipp Rahe, David Necas
    ''' *  E-mail: hquerquadrat@gmail.com
    ''' </summary>
    Public Function ImportBias(ByRef FullFileNamePlusPath As String,
                               ByVal FetchOnlyFileHeader As Boolean,
                               Optional ByRef ReaderBuffer As String = "",
                               Optional ByRef FilesToIgnoreAfterThisImport As List(Of String) = Nothing) As cSpectroscopyTable Implements iFileImport_SpectroscopyTable.ImportSpectroscopyTable

        ' Check, if the ignore list is nothing. If yes, then create a new list.
        If FilesToIgnoreAfterThisImport Is Nothing Then FilesToIgnoreAfterThisImport = New List(Of String)

        ' First get the file base name from the individual file name.
        Dim FileName As String = IO.Path.GetFileName(FullFileNamePlusPath)
        Dim FileDirectory As New DirectoryInfo(IO.Path.GetDirectoryName(FullFileNamePlusPath))
        Dim FileListInDirectory As FileInfo() = FileDirectory.GetFiles()

        ' Get the base name of the file.
        Dim BaseFileNameComponents As SpectroscopyFileName = Me.GetSpectroscopyFileNameComponents(FileName)

        '################################################################
        ' First of all, search for the parameter file, if it is present.
        ' It consists out of the "base name"+"_0001.mtrx".
        Dim ParameterFile As cFileImportOmicronMatrixParameterFile = Nothing
        For Each oFile As FileInfo In FileListInDirectory
            If oFile.Name = BaseFileNameComponents.BaseName & "_0001.mtrx" Then
                ' Parameter-File found.

                ReaderBuffer = ""

                ' Get the parameter file.
                ParameterFile = cFileImportOmicronMatrixParameterFile.ReadParameterFile(oFile.FullName)

                ' Exit the loop, since we have found the file.
                Exit For
            End If
        Next
        '#######################################################

        ' Create new SpectroscopyTable object.
        Dim oSpectroscopyTable As New cSpectroscopyTable
        oSpectroscopyTable.FullFileName = FullFileNamePlusPath

        ' Omicron Matrix files consist out of a separate file for each trace.
        ' So we collect a list of all files from the folder that match with the basename.
        For Each oFile As FileInfo In FileListInDirectory

            Dim CurrentFileNameComponents As SpectroscopyFileName = Me.GetSpectroscopyFileNameComponents(oFile.Name)

            '#######################################################
            ' Search for all files of the same dataset.
            ' This is the case, if the basename matches, the curve number, and the unit!
            If CurrentFileNameComponents.BaseName = BaseFileNameComponents.BaseName AndAlso
               CurrentFileNameComponents.CurveNumber = BaseFileNameComponents.CurveNumber AndAlso
               CurrentFileNameComponents.DataIsUnitOf = BaseFileNameComponents.DataIsUnitOf Then

                '###########################################
                ' Get the measurement configuration for the data.
                ' This is necessary for getting the correct units and transferfunctions.
                Dim MeasurementConfig As cFileImportOmicronMatrixParameterFile.MeasurementDetails
                Dim TFF As TransferFunction
                Dim ChannelID As Integer = -1
                Dim ChannelUnit As String = ""

                ' If the Parameterfile was found, get the correct Transferfunctions and channel units!
                If ParameterFile IsNot Nothing Then
                    MeasurementConfig = ParameterFile.GetMeasurementSettingsRelatedToFileName(oFile.Name)
                    ChannelID = MeasurementConfig.GetChannelIDFromMeasurement(CurrentFileNameComponents.ChannelNameInclDependingUnit)
                    If ChannelID >= 0 Then
                        TFF = MeasurementConfig.TransferFunctions(ChannelID)
                        ChannelUnit = MeasurementConfig.Channels(ChannelID).Value
                    Else
                        TFF = New TransferFunction
                    End If
                Else
                    TFF = New TransferFunction
                End If

                ' Load StreamReader with the Big Endian Encoding
                Dim fs As New FileStream(oFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read)
                ' Now Using BinaryReader to obtain Image-Data
                Dim br As New BinaryReader(fs, System.Text.Encoding.Default)

                ' Stores the data
                Dim Data As New List(Of Double)

                Try

                    ' Buffers
                    Dim LastFourChars As String = String.Empty
                    ReaderBuffer = ""

                    Dim ExpectedNumber As UInt32
                    Dim RecordedNumber As UInt32
                    Dim RecordTimeTicks As ULong

                    ' Read the header up to the position of the data, which is announced by "ATAD".
                    Do Until fs.Position = fs.Length

                        ' Move the identifier buffer by one byte.
                        LastFourChars = GetLastFourCharsByProceedingOneByte(br, LastFourChars)

                        Select Case LastFourChars

                            Case "TLKB"
                                ' BKLT: This announces the timestamp at which the file has been recorded.

                                ' BLOCK READING NOT WORKING HERE???
                                'Dim CurrentBlock As DataBlockWithTime = ReadBlockWithTime(br, LastFourChars)
                                'Using BlockReader As BinaryReader = GetBinaryReaderForBlockContent(CurrentBlock)

                                '    ' Store the time as record time.
                                '    oSpectroscopyTable.RecordDate = CurrentBlock.Time

                                'End Using

                                ' Jump over the first 4 bytes!
                                fs.Seek(4, SeekOrigin.Current)

                                ' Timestamp of the file. The next 8 bytes.
                                RecordTimeTicks = br.ReadUInt64
                                oSpectroscopyTable.RecordDate = New DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(RecordTimeTicks).ToLocalTime()

                            Case "CSED"
                                ' DESC: This is the descriptor of the file.
                                ' E.g. it announces the number of points in the curve.

                                Dim CurrentBlock As DataBlockWithoutTime = ReadBlockWithoutTime(br, LastFourChars)
                                Using BlockReader As BinaryReader = GetBinaryReaderForBlockContent(CurrentBlock)

                                    ' The next 20 bytes are unknown.
                                    BlockReader.BaseStream.Seek(20, SeekOrigin.Current)

                                    ' The next 4 bytes are UINT32-LE as point number.
                                    ExpectedNumber = BlockReader.ReadUInt32
                                    RecordedNumber = BlockReader.ReadUInt32
                                    oSpectroscopyTable.MeasurementPoints = Convert.ToInt32(RecordedNumber)

                                End Using

                            Case "ATAD"
                                ' DATA: This announces that the data is following.

                                ' Abort reading of the header. From here on the data starts.
                                If FetchOnlyFileHeader Then
                                    Exit Do
                                End If

                                Dim CurrentBlock As DataBlockWithoutTime = ReadBlockWithoutTime(br, LastFourChars)
                                Using BlockReader As BinaryReader = GetBinaryReaderForBlockContent(CurrentBlock)

                                    ' read the data
                                    Dim Value As Int32
                                    While BlockReader.BaseStream.Position < BlockReader.BaseStream.Length
                                        Value = BlockReader.ReadInt32
                                        Data.Add(GetValueByTransferFunction(Value, TFF))
                                    End While

                                End Using

                        End Select

                    Loop
                Catch ex As Exception
                    Debug.WriteLine("cFileImportOmicronMatrixCurveFile: Error reading spectroscopy file: " & ex.Message)
                Finally
                    br.Close()
                    fs.Close()
                    br.Dispose()
                    fs.Dispose()
                End Try

                '##############################################################################
                ' GENERATE X-Column
                ' Get the Matrix specific sweep parameters for the spectroscopy file.
                ' The Matrix system has two sweep devices. We decide which one to use by
                ' the unit of the system. Device 1 is usually VOLT and Device 2 usually Meter.

                If ParameterFile IsNot Nothing Then

                    ' Check, if the column should be generated.
                    Dim CreateXColumn As Boolean = True

                    ' Get the Spectroscopy-properties for the file
                    Dim SpecProp As SpectroscopyProperties = GetSpectroscopyPropertiesFromPropertyArray(oFile.Name, ParameterFile)
                    ' Get the XYScanner-properties for the file
                    Dim XYScanner As XYScannerProperties = GetXYScannerPropertiesFromPropertyArray(oFile.Name, ParameterFile)

                    ' From the sweep details we create now the X-column of the data.
                    ' First we have to find out, whether to use device 1 or 2
                    Dim SweepStart As Double = 0
                    Dim SweepEnd As Double = 0
                    Dim ForwardBackward As Boolean = False
                    Dim SweepNumber As Integer = 0
                    Dim ChannelUnitType As cUnits.UnitType = cUnits.GetUnitTypeFromSymbol(CurrentFileNameComponents.DataIsUnitOf)

                    Select Case ChannelUnitType
                        Case cUnits.GetUnitTypeFromSymbol(SpecProp.Device1_UnitString)
                            SweepStart = SpecProp.Device1_SweepStart
                            SweepEnd = SpecProp.Device1_SweepEnd
                            ForwardBackward = SpecProp.Device1_ForwardAndBackward
                            SweepNumber = SpecProp.Device1_SweepNumber
                        Case cUnits.GetUnitTypeFromSymbol(SpecProp.Device2_UnitString)
                            SweepStart = SpecProp.Device2_SweepStart
                            SweepEnd = SpecProp.Device2_SweepEnd
                            ForwardBackward = SpecProp.Device2_ForwardAndBackward
                            SweepNumber = SpecProp.Device2_SweepNumber
                        Case Else
                            CreateXColumn = False
                    End Select

                    ' Set some general parameters of the file
                    With oSpectroscopyTable
                        .Backward_Sweep = ForwardBackward
                        .FeedbackOff = SpecProp.DisableFeedback
                        '.FeedbackOpenBias_V = ??
                        '.FeedbackOpenCurrent_A = ??
                        .Location_X = SpecProp.Location.XCoord
                        .Location_Y = SpecProp.Location.YCoord
                        '.Location_Z = SpecProp.Location.ZCoord
                        .NumberOfSweeps = SweepNumber

                        .ZController_PGain = XYScanner.ProportionalGain
                        .ZController_Setpoint = XYScanner.Setpoint
                        .ZController_SetpointUnit = XYScanner.SetpointUnit

                        If ChannelUnitType = cUnits.UnitType.Voltage Then
                            .BiasSpec_SweepStart_V = SweepStart
                            .BiasSpec_SweepEnd_V = SweepEnd
                        ElseIf ChannelUnitType = cUnits.UnitType.Length Then
                            .Z_Sweep_Distance = SweepStart - SweepEnd
                            '.Z_Spectroscopy_Bias_V = XYScanner.
                        End If
                    End With


                    ' Now generate the X-column, if we have a known channel unit type.
                    If ChannelUnitType <> cUnits.UnitType.Unknown Then

                        ' Check if the X-column under the same name exists already
                        If ChannelUnitType = cUnits.UnitType.Length AndAlso oSpectroscopyTable.GetColumnNameList.Contains(XColumnName_Length) Then
                            CreateXColumn = False
                        End If
                        If ChannelUnitType = cUnits.UnitType.Voltage AndAlso oSpectroscopyTable.GetColumnNameList.Contains(XColumnName_Voltage) Then
                            CreateXColumn = False
                        End If

                        ' Create the X-column
                        If CreateXColumn Then
                            Dim XColumn As New cSpectroscopyTable.DataColumn

                            ' Load the count of the data, depending on forward/backward sweep.
                            Dim ActualDataCount As Integer
                            If oSpectroscopyTable.Backward_Sweep Then
                                ActualDataCount = CInt(Data.Count / 2)
                            Else
                                ActualDataCount = Data.Count
                            End If

                            ' Create the columndata list with the actual data count.
                            Dim XColumnData As New List(Of Double)(ActualDataCount)

                            ' Generate the data, if we have data in the other column.
                            If ActualDataCount > 0 Then
                                Dim TMPVal As Double
                                Dim SweepStep As Double = (SweepEnd - SweepStart) / ActualDataCount

                                For i As Integer = 0 To ActualDataCount - 1 Step 1

                                    TMPVal = SweepStart + i * SweepStep
                                    XColumnData.Add(TMPVal)

                                Next
                            End If


                            With XColumn
                                .IsSpectraFoxGenerated = False
                                If ChannelUnitType = cUnits.UnitType.Length Then
                                    .Name = XColumnName_Length
                                ElseIf ChannelUnitType = cUnits.UnitType.Voltage Then
                                    .Name = XColumnName_Voltage
                                End If
                                .UnitSymbol = ChannelUnit
                                .UnitType = cUnits.GetUnitTypeFromSymbol(.UnitSymbol)
                                .SetValueList(XColumnData)
                            End With

                            oSpectroscopyTable.AddNonPersistentColumn(XColumn)
                        End If
                    End If

                End If

                '#########################################
                ' Finally setup the actual data column.
                ' Consider here, that we may have forward and backward columns.
                If oSpectroscopyTable.Backward_Sweep Then

                    ' Create both data DataColumns.
                    Dim DataColFW As New cSpectroscopyTable.DataColumn
                    Dim DataColBW As New cSpectroscopyTable.DataColumn
                    Dim ActualDataCount As Integer = CInt(Data.Count / 2)
                    oSpectroscopyTable.MeasurementPoints = ActualDataCount

                    ' Set the values of the columns.
                    With DataColFW
                        .IsSpectraFoxGenerated = False
                        .Name = CurrentFileNameComponents.CurveName & " FW"
                        .UnitSymbol = ChannelUnit
                        .UnitType = cUnits.GetUnitTypeFromSymbol(.UnitSymbol)
                        .SetValueList(Data.GetRange(0, ActualDataCount))
                    End With
                    With DataColBW
                        .IsSpectraFoxGenerated = False
                        .Name = CurrentFileNameComponents.CurveName & " BW"
                        .UnitSymbol = ChannelUnit
                        .UnitType = cUnits.GetUnitTypeFromSymbol(.UnitSymbol)
                        Dim DataBW As List(Of Double) = Data.GetRange(ActualDataCount, ActualDataCount)
                        DataBW.Reverse()
                        .SetValueList(DataBW)
                    End With

                    ' Finally add the columns to the SpectroscopyTable
                    oSpectroscopyTable.AddNonPersistentColumn(DataColFW)
                    oSpectroscopyTable.AddNonPersistentColumn(DataColBW)

                Else

                    ' Create a new DataColumn.
                    Dim DataCol As New cSpectroscopyTable.DataColumn

                    ' Set the values of the column.
                    With DataCol
                        .IsSpectraFoxGenerated = False
                        .Name = CurrentFileNameComponents.CurveName
                        .UnitSymbol = ChannelUnit
                        .UnitType = cUnits.GetUnitTypeFromSymbol(.UnitSymbol)
                        .SetValueList(Data)
                    End With

                    ' Finally add the column to the SpectroscopyTable
                    oSpectroscopyTable.AddNonPersistentColumn(DataCol)

                End If

                ' Ignore this file for further imports, since we already imported it.
                FilesToIgnoreAfterThisImport.Add(oFile.FullName)

            End If
            '#######################################################

        Next

        ' Add a separate Column just for the measurement point number
        Dim PointColumn As New cSpectroscopyTable.DataColumn
        With PointColumn
            .Name = My.Resources.ColumnName_MeasurementPoints
            .IsSpectraFoxGenerated = False
            .UnitSymbol = "1"
            .UnitType = cUnits.UnitType.Unitary
            Dim PointData As New List(Of Double)(oSpectroscopyTable.MeasurementPoints)
            If Not FetchOnlyFileHeader Then
                For i As Integer = 1 To oSpectroscopyTable.MeasurementPoints Step 1
                    PointData.Add(i)
                Next
            End If
            .SetValueList(PointData)
        End With
        oSpectroscopyTable.AddNonPersistentColumn(PointColumn)

        ' File Exists, so set the property.
        oSpectroscopyTable._bFileExists = True

        Return oSpectroscopyTable
    End Function

    ''' <summary>
    ''' File-Extension
    ''' </summary>
    Public ReadOnly Property FileExtension As String Implements iFileImport_SpectroscopyTable.FileExtension
        Get
            Return "_mtrx"
        End Get
    End Property

    ''' <summary>
    ''' Checks, if the given file is a known Omicron-Matrix spectroscopy image file
    ''' </summary>
    Public Function IdentifyFile(ByRef FullFileNamePlusPath As String,
                                 Optional ByRef ReaderBuffer As String = "") As Boolean Implements iFileImport_SpectroscopyTable.IdentifyFile

        ' Load StreamReader and read first identifier.
        ' Is the only one needed for Identification.
        Dim sr As New StreamReader(FullFileNamePlusPath)
        Dim Buffer(DataFileIdentifier.Length - 1) As Char
        sr.ReadBlock(Buffer, 0, DataFileIdentifier.Length)
        sr.Close()
        sr.Dispose()

        ' All Omicron data files start with the DataFileIdentifier.
        ' If we stumble upon this identifier, we can start loading the file.
        ' For the individual we can then load the parameter file separately.
        If Buffer = DataFileIdentifier Then

            ' Now check, if the file is a SpectroscopyFile.
            ' This is done from the file-extension, which should contain
            ' the curve name, e.g. I(V)_mtrx.
            Dim FileExtension As String = IO.Path.GetExtension(FullFileNamePlusPath)
            If SpectroscopyFileExtension.IsMatch(FileExtension) Then
                Return True
            End If

        End If

        Return False
    End Function

End Class
