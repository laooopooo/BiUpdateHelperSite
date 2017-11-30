﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BPUtil;
using Newtonsoft.Json;
using SQLite;

namespace BiUpdateHelperSite.DB
{
	public static class Agent
	{
		private static SQLiteConnection db;
		static Agent()
		{
			db = new SQLiteConnection(MainStatic.settings.dbPath);
			db.CreateTable<UsageRecord>();
			db.CreateTable<Camera>();
			db.CreateTable<GpuInfo>();
		}
		public static List<UsageRecord> GetAllUsageRecords()
		{
			return db.Table<UsageRecord>().ToList();
		}
		public static List<UsageRecord> GetAllUsageRecords_TimestampDesc()
		{
			return db.Table<UsageRecord>().OrderByDescending(r => r.Timestamp).ToList();
		}
		public static List<UsageRecord> GetUsageRecordsByCPU(string CPU)
		{
			return db.Query<UsageRecord>(@"SELECT * FROM UsageRecord WHERE CPU LIKE ?", CPU);
		}
		public static UsageRecord GetUsageRecord(int ID)
		{
			return db.Get<UsageRecord>(ID);
		}
		public static List<Camera> GetAllCameras()
		{
			return db.Table<Camera>().ToList();
		}
		public static List<Camera> GetCameras(int UsageRecordID)
		{
			return db.Query<Camera>(@"SELECT * FROM Camera WHERE UsageRecordID = ?", UsageRecordID);
		}
		public static Camera GetCamera(int CameraID)
		{
			return db.Get<Camera>(CameraID);
		}
		public static List<GpuInfo> GetAllGpus()
		{
			return db.Table<GpuInfo>().ToList();
		}
		public static List<GpuInfo> GetGpus(int UsageRecordID)
		{
			return db.Query<GpuInfo>(@"SELECT * FROM GpuInfo WHERE UsageRecordID = ?", UsageRecordID);
		}
		public static GpuInfo GetGpu(int GpuID)
		{
			return db.Get<GpuInfo>(GpuID);
		}

		public static void AddUsageRecord(UsageRecordUpload.v1.Upload_Record obj)
		{
			// Create objects
			UsageRecord record = new UsageRecord();
			record.Secret = obj.Secret;
			record.Timestamp = TimeUtil.GetTimeInMsSinceEpoch();
			record.OS = obj.OS;
			record.HelperVersion = obj.HelperVersion;
			record.BiVersion = obj.BiVersion;
			record.CpuModel = obj.CpuModel;
			record.CpuMHz = obj.CpuMHz;
			record.CpuUsage = obj.CpuUsage;
			record.BiCpuUsage = obj.BiCpuUsage;
			record.MemMB = obj.MemMB;
			record.BiMemUsageMB = obj.BiMemUsageMB;
			record.BiPeakVirtualMemUsageMB = obj.BiPeakVirtualMemUsageMB;
			record.MemFreeMB = obj.MemFreeMB;
			record.HwAccel = (HWAccel)obj.HwAccel;
			record.RamGiB = obj.RamGiB;
			record.RamChannels = obj.RamChannels;
			record.RamMHz = obj.RamMHz;

			List<Camera> cameras = new List<Camera>();
			record.CameraCount = (byte)Math.Min(255, obj.cameras.Length);
			record.Total_FPS = record.Total_Megapixels = record.Total_MPPS = 0;
			foreach (var cam in obj.cameras)
			{
				Camera camera = new Camera();
				camera.Pixels = cam.Pixels;
				camera.FPS = cam.FPS;
				camera.LimitDecode = cam.LimitDecode;
				camera.Hwaccel = (HWAccelCamera)cam.Hwaccel;
				camera.Type = (CameraType)cam.Type;
				camera.CapType = (ScreenCapType)cam.CapType;
				camera.MotionDetector = cam.MotionDetector;
				camera.RecordTriggerType = (RecordingTriggerType)cam.RecordTriggerType;
				camera.RecordFormat = (RecordingFormat)cam.RecordFormat;
				camera.DirectToDisk = cam.DirectToDisk;
				camera.VCodec = cam.VCodec;
				float MP = cam.Pixels / 1000000f;
				record.Total_Megapixels += MP;
				record.Total_FPS += cam.FPS;
				record.Total_MPPS += MP * cam.FPS;
				cameras.Add(camera);
			}

			db.RunInTransaction(() =>
			{
				List<UsageRecord> existing = db.Query<UsageRecord>("SELECT * FROM UsageRecord WHERE Secret = ? AND CpuModel = ?", record.Secret, record.CpuModel);
				if (existing.Count > 0)
				{
					record.ID = existing[0].ID;
					int rC = db.Execute("DELETE FROM Camera WHERE UsageRecordID = ?", record.ID);
					int rG = db.Execute("DELETE FROM GpuInfo WHERE UsageRecordID = ?", record.ID);
					db.Update(record);
				}
				else
				{
					// Add usage record to DB
					int rowsAffected = db.Insert(record);
					if (rowsAffected == 0)
					{
						Logger.Debug("Failed to insert record: " + JsonConvert.SerializeObject(record));
						return;
					}
				}

				// Finish creating camera records and add them to the DB.
				foreach (Camera camera in cameras)
				{
					camera.UsageRecordID = record.ID;
					Logger.Info("Inserting Camera for Record: " + camera.UsageRecordID);
					db.Insert(camera);
				}

				// Add Gpu records to the DB.
				foreach (var gpu in obj.gpus)
				{
					GpuInfo gi = new GpuInfo();
					gi.UsageRecordID = record.ID;
					gi.Name = gpu.Name;
					gi.DriverVersion = gpu.Version;
					Logger.Info("Inserting GPU for Record: " + gi.UsageRecordID);
					db.Insert(gi);
				}
			});
		}

		public static bool DeleteUsageRecord(int ID)
		{
			try
			{
				db.RunInTransaction(() =>
				{
					db.Delete<UsageRecord>(ID);
					db.Execute("DELETE FROM Camera WHERE UsageRecordID = ?", ID);
					db.Execute("DELETE FROM GpuInfo WHERE UsageRecordID = ?", ID);
				});
				return true;
			}
			catch (Exception ex)
			{
				Logger.Debug(ex);
				return false;
			}
		}
	}
}