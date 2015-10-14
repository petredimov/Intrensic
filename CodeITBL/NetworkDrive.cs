﻿using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeITBL
{
	public class NetworkDrive
	{
		private readonly ILog _logger = LogManager.GetLogger(typeof(NetworkConnection));
		#region API
		[DllImport("mpr.dll")]
		private static extern int WNetAddConnection2A(ref structNetResource pstNetRes, string psPassword, string psUsername, int piFlags);
		[DllImport("mpr.dll")]
		private static extern int WNetCancelConnection2A(string psName, int piFlags, int pfForce);
		[DllImport("mpr.dll")]
		private static extern int WNetConnectionDialog(int phWnd, int piType);
		[DllImport("mpr.dll")]
		private static extern int WNetDisconnectDialog(int phWnd, int piType);
		[DllImport("mpr.dll")]
		private static extern int WNetRestoreConnectionW(int phWnd, string psLocalDrive);

		[DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern int WNetGetConnection(
			[MarshalAs(UnmanagedType.LPTStr)] string localName,
			[MarshalAs(UnmanagedType.LPTStr)] StringBuilder remoteName,
			ref int length);

		[StructLayout(LayoutKind.Sequential)]
		private struct structNetResource
		{
			public int iScope;
			public int iType;
			public int iDisplayType;
			public int iUsage;
			public string sLocalName;
			public string sRemoteName;
			public string sComment;
			public string sProvider;
		}

		private const int RESOURCETYPE_DISK = 0x1;

		//Standard	
		private const int CONNECT_INTERACTIVE = 0x00000008;
		private const int CONNECT_PROMPT = 0x00000010;
		private const int CONNECT_UPDATE_PROFILE = 0x00000001;
		//IE4+
		private const int CONNECT_REDIRECT = 0x00000080;
		//NT5 only
		private const int CONNECT_COMMANDLINE = 0x00000800;
		private const int CONNECT_CMD_SAVECRED = 0x00001000;

		#endregion

		#region Propertys and options
		private bool lf_SaveCredentials = false;
		/// <summary>
		/// Option to save credentials are reconnection...
		/// </summary>
		public bool SaveCredentials
		{
			get { return (lf_SaveCredentials); }
			set { lf_SaveCredentials = value; }
		}
		private bool lf_Persistent = false;
		/// <summary>
		/// Option to reconnect drive after log off / reboot ...
		/// </summary>
		public bool Persistent
		{
			get { return (lf_Persistent); }
			set { lf_Persistent = value; }
		}
		private bool lf_Force = false;
		/// <summary>
		/// Option to force connection if drive is already mapped...
		/// or force disconnection if network path is not responding...
		/// </summary>
		public bool Force
		{
			get { return (lf_Force); }
			set { lf_Force = value; }
		}
		private bool ls_PromptForCredentials = false;
		/// <summary>
		/// Option to prompt for user credintals when mapping a drive
		/// </summary>
		public bool PromptForCredentials
		{
			get { return (ls_PromptForCredentials); }
			set { ls_PromptForCredentials = value; }
		}

		private string ls_Drive = "s:";
				
		/// <summary>
		/// Drive to be used in mapping / unmapping...
		/// </summary>
		public string LocalDrive
		{
			get { return (ls_Drive); }
			set
			{
				if (value.Length >= 1)
				{
					ls_Drive = value.Substring(0, 1) + ":";
				}
				else
				{
					ls_Drive = "";
				}
			}
		}
		private string ls_ShareName = "\\\\Computer\\C$";
		/// <summary>
		/// Share address to map drive to.
		/// </summary>
		public string ShareName
		{
			get { return (ls_ShareName); }
			set { ls_ShareName = value; }
		}
		#endregion

		#region Function mapping
		/// <summary>
		/// Map network drive
		/// </summary>
		public void MapDrive() { zMapDrive(null, null); }
		/// <summary>
		/// Map network drive (using supplied Password)
		/// </summary>
		public void MapDrive(string Password) { zMapDrive(null, Password); }
		/// <summary>
		/// Map network drive (using supplied Username and Password)
		/// </summary>
		public void MapDrive(string Username, string Password) { zMapDrive(Username, Password); }
		/// <summary>
		/// Unmap network drive
		/// </summary>
		public void UnMapDrive() { zUnMapDrive(this.lf_Force); }
		/// <summary>
		/// Check / restore persistent network drive
		/// </summary>
		public void RestoreDrives() { zRestoreDrive(); }
		/// <summary>
		/// Display windows dialog for mapping a network drive
		/// </summary>
		public void ShowConnectDialog(Form ParentForm) { zDisplayDialog(ParentForm, 1); }
		/// <summary>
		/// Display windows dialog for disconnecting a network drive
		/// </summary>
		public void ShowDisconnectDialog(Form ParentForm) { zDisplayDialog(ParentForm, 2); }


		#endregion

		#region Core functions

        public static IPAddress ResolveHostNameToIP(string hostname, AddressFamily addressVersion = AddressFamily.InterNetwork)
        {
            try
            {
                if (!String.IsNullOrEmpty(hostname))
                {
                    List<IPAddress> addresses = new List<IPAddress>(Dns.GetHostAddresses(hostname));

                    return addresses.FindLast(ip => ip.AddressFamily == addressVersion);
                }

                return null;
            }
            catch (Exception ex)
            {
                
            }
            return null;
        }

		// Map network drive
		private void zMapDrive(string psUsername, string psPassword)
		{
            ls_ShareName = ls_ShareName.TrimEnd('\\');

            try
            {
                string ipadress = "";
                string hostname = "";

                try
                {
                    if (ls_ShareName.Split('\\').Length > 2)
                    {
                        hostname = ls_ShareName.Split('\\')[2];
                        if (!String.IsNullOrEmpty(hostname))
                        {
                            IPAddress address = ResolveHostNameToIP(hostname);
                            if (address != null)
                            {
                                ipadress = address.ToString();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex.Message);
                }

                if (!string.IsNullOrEmpty(ipadress))
                {
                    ls_ShareName = ls_ShareName.Replace(hostname, ipadress);
                }

                int retries = 5;
                int cnt = 0;
                bool returnValue = false;
                string errorStr = string.Empty;
                string outPath = string.Empty;
                string notExistedPathPart = string.Empty;
                //create struct data
                structNetResource stNetRes = new structNetResource();
                stNetRes.iScope = 2;
                stNetRes.iType = RESOURCETYPE_DISK;
                stNetRes.iDisplayType = 3;
                stNetRes.iUsage = 1;
                stNetRes.sRemoteName = ls_ShareName;
                stNetRes.sLocalName = ls_Drive;

                //prepare params
                int iFlags = 0;
                if (lf_SaveCredentials) { iFlags += CONNECT_CMD_SAVECRED; }
                if (lf_Persistent) { iFlags += CONNECT_UPDATE_PROFILE; }
                if (ls_PromptForCredentials) { iFlags += CONNECT_INTERACTIVE + CONNECT_PROMPT; }
                if (psUsername == "") { psUsername = null; }
                if (psPassword == "") { psPassword = null; }
                //if force, unmap ready for new connection
                if (lf_Force) { try { zUnMapDrive(true); } catch { } }
                //call and return
                while ((!returnValue) && (cnt < retries))
                {
                    errorStr = string.Empty;
                    cnt++;
                    int res = WNetAddConnection2A(ref stNetRes, psPassword, psUsername, iFlags);
                    _logger.Info("Result from mapping:" + res);
                    if (res > (int)NetworkError.EROOR_SUCCESS)
                    {
                        if (res == (int)NetworkError.ERROR_BAD_NETPATH)
                        {
                            // possible reason non existed part of the path, try to remove a last folder name.
                            int lastIndex = ls_ShareName.LastIndexOf(@"\");
                            if (lastIndex > 2)
                            {
                                notExistedPathPart = ls_ShareName.Substring(lastIndex, ls_ShareName.Length - lastIndex) + notExistedPathPart;
                                ls_ShareName = ls_ShareName.Substring(0, lastIndex);
                                stNetRes.sRemoteName = ls_ShareName;
                                _logger.Warn("ERROR_BAD_NETPATH");
                            }
                            continue;
                        }
                        else if (res == (int)NetworkError.MULTIPLE_CONNECTIONS_TO_A_SERVER_OR_SHARED_RESOURCE_BY_THE_SAME_USER)
                        {
                            _logger.Warn("MULTIPLE_CONNECTIONS_TO_A_SERVER_OR_SHARED_RESOURCE_BY_THE_SAME_USER");
                            // Drive exist find it and just use it
                            foreach (DriveInfo drive in DriveInfo.GetDrives())
                            {
                                if (drive.GetType().ToString() == "Network" && GetUNCPath(drive.Name) == GetUNCPath(ls_ShareName))
                                {
                                    outPath = string.Format(@"{0}:\{1}", ls_Drive, notExistedPathPart.TrimStart('\\'));
                                    returnValue = true;
                                    break;
                                }
                            }
                        }
                        //errorStr = "MapDrive error:" + res.ToString();
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        _logger.Info("The path is mapped.");
                        outPath = string.Format(@"{0}:\{1}", ls_Drive, notExistedPathPart.TrimStart('\\'));
                        returnValue = true;
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.Error("Failed to map: " + ls_ShareName);
            }

			
			

			//if (res > 0) { throw new System.ComponentModel.Win32Exception(i); }
		}

		public static string GetUNCPath(string originalPath)
        {
            bool tmp;
            return GetUNCPath(originalPath, out tmp);
        }
        public static string GetUNCPath(string originalPath, out bool isMappedDrive)
        {
            StringBuilder sb = new StringBuilder(512);
            int size = sb.Capacity;
            isMappedDrive = false;

            // look for the {LETTER}: combination ...
            if (originalPath.Length > 2 && originalPath[1] == ':')
            {
                // don't use char.IsLetter here - as that can be misleading
                // the only valid drive letters are a-z && A-Z.
                char c = originalPath[0];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                {
                    int error = WNetGetConnection(originalPath.Substring(0, 2),
                        sb, ref size);
                    if (error == 0)
                    {
                        isMappedDrive = true;
                        string path = Path.GetFullPath(originalPath).Substring(Path.GetPathRoot(originalPath).Length);
                        return Path.Combine(sb.ToString().TrimEnd(), path);
                    }
                }
            }

            return originalPath;
        }


		// Unmap network drive	
		private void zUnMapDrive(bool pfForce)
		{
			//call unmap and return
			int iFlags = 0;
			if (lf_Persistent) { iFlags += CONNECT_UPDATE_PROFILE; }
			int i = WNetCancelConnection2A(ls_Drive, iFlags, Convert.ToInt32(pfForce));
			if (i != 0) i = WNetCancelConnection2A(ls_ShareName, iFlags, Convert.ToInt32(pfForce));  //disconnect if localname was null
			if (i != 0)
			{
				string msg = string.Empty;
				UnMapDriveWithCommand(LocalDrive, out msg);
			}
		}

		private bool UnMapDriveWithCommand(string drive, out string msg)
		{
			msg = "DisconnectDrive: disconnect drive by WNetCancelConnection2A fault: ";
			bool result = false;
			Process p = null;

			try
			{
				p = new Process();
				p.StartInfo.UseShellExecute = false;
				p.StartInfo.CreateNoWindow = true;
				p.StartInfo.RedirectStandardError = true;

				p.StartInfo.FileName = "net.exe";
				p.StartInfo.Arguments = string.Format(" USE {0}: /DELETE /Y", LocalDrive);
				p.Start();
				p.WaitForExit(3000);

				string errorMessage = p.StandardError.ReadToEnd();

				if (errorMessage.Length == 0)
				{
					result = true;
				}
				else
				{
					msg += ("\nDisconnectDrive: disconnect drive by net.exe fault: " + errorMessage);
				}
			}
			catch (Exception ex)
			{
				_logger.Error(ex);
			}

			finally
			{
				if (p != null)
				{
					p.Close();
					p.Dispose();
				}
			}

			return result;
		}

		// Check / Restore a network drive
		private void zRestoreDrive()
		{
			//call restore and return
			int i = WNetRestoreConnectionW(0, null);
			if (i > 0) { throw new System.ComponentModel.Win32Exception(i); }
		}

		// Display windows dialog
		private void zDisplayDialog(Form poParentForm, int piDialog)
		{
			int i = -1;
			int iHandle = 0;
			//get parent handle
			if (poParentForm != null)
			{
				iHandle = poParentForm.Handle.ToInt32();
			}
			//show dialog
			if (piDialog == 1)
			{
				i = WNetConnectionDialog(iHandle, RESOURCETYPE_DISK);
			}
			else if (piDialog == 2)
			{
				i = WNetDisconnectDialog(iHandle, RESOURCETYPE_DISK);
			}
			if (i > 0) { throw new System.ComponentModel.Win32Exception(i); }
			//set focus on parent form
			poParentForm.BringToFront();
		}


		#endregion

	}
}
