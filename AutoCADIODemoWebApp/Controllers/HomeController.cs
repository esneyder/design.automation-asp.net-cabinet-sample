﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Text.RegularExpressions;

using System.Text;
using System.IO;
using RestSharp;

namespace MvcApplication2.Controllers
{

    public class ForgeSrv{


        //when you test this sample, check with developer portal of Forge if any endpoints are updated 
        //and also the request/response format of the updated endpoint
        //https://developer.autodesk.com


        public static string forgeServiceBaseUrl = "https://developer.api.autodesk.com";
        public static string autenticationUrl = "authentication/v1/authenticate";
        public static string createBucketUrl = "oss/v2/buckets";
        public static string uploadObjToBucketUrl = "oss/v2/buckets/{0}/objects/{1}";
        public static string transJobUrl = "modelderivative/v2/designdata/job";
        public static string transJobStatusUrl = "modelderivative/v2/designdata/{0}/manifest";

        //make sure the activity has been created by any other program!
        public static string designAuto_Act_Id = "CreateCloset"; 
    }

    public class HomeController : Controller
    {
        // Closet parameters
        private static string _width = String.Empty;
        private static string _depth = String.Empty;
        private static string _height = String.Empty;
        private static string _plyThickness = String.Empty;
        private static string _doorHeightPercentage = String.Empty;
        private static string _numberOfDrawers = String.Empty;
        private static int _iNumOfDrawers = 1;
        private static string _isSplitDrawers = "Yes";
        private static string _emailAddress = String.Empty;

        // View and Data API 
        RestClient _client = new RestClient(ForgeSrv.forgeServiceBaseUrl);
        static String _accessToken = String.Empty;

        //make sure you have created a bucket with this name
        static String _bucketName = Properties.Settings.Default.ForgeBucket;

        static Boolean _bucketFound = false;
        static String _closetDrawingPath = String.Empty; // for email attachment
        static String _imagePath = String.Empty; //  for email attachment
        static String _fileUrn = String.Empty; // For viewing using View & Data API

        public HomeController()
        {
            // Set up Design Automation
            Autodesk.AcadIOUtils.SetupAutoCADIOContainer(Properties.Settings.Default.ForgeClientId, Properties.Settings.Default.ForgeClientSecret);

            Autodesk.GeneralUtils.S3BucketName = Properties.Settings.Default.S3BucketName;

            // Set up Forge Data Management, Derivitive, Viewer
            SetupViewer();
            //*/
        }

        void SetupViewer()
        {
            // Authentication
            bool authenticationDone = false;

            RestRequest authReq = new RestRequest();
            authReq.Resource = ForgeSrv.autenticationUrl;
            authReq.Method = Method.POST;
            authReq.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            authReq.AddParameter("client_id", Properties.Settings.Default.ForgeClientId );
            authReq.AddParameter("client_secret", Properties.Settings.Default.ForgeClientSecret );
            authReq.AddParameter("grant_type", "client_credentials");
            authReq.AddParameter("scope", "data:read data:write bucket:create bucket:read");

            IRestResponse result = _client.Execute(authReq);
            if (result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                String responseString = result.Content;
                int len = responseString.Length;
                int index = responseString.IndexOf("\"access_token\":\"") + "\"access_token\":\"".Length;
                responseString = responseString.Substring(index, len - index - 1);
                int index2 = responseString.IndexOf("\"");
                _accessToken = responseString.Substring(0, index2);

                authenticationDone = true; 
            }

            if (!authenticationDone)
            {
                ViewData["Message"] = "Forge authentication failed !";
                _accessToken = String.Empty;
                return;
            }

            RestRequest bucketReq = new RestRequest();
            bucketReq.Resource = ForgeSrv.createBucketUrl ;
            bucketReq.Method = Method.POST;
            bucketReq.AddParameter("Authorization", "Bearer " + _accessToken, ParameterType.HttpHeader);
            bucketReq.AddParameter("Content-Type", "application/json", ParameterType.HttpHeader);

            //bucketname is the name of the bucket.
            string body = "{\"bucketKey\":\"" + _bucketName + "\",\"policyKey\":\"transient\"}";
            bucketReq.AddParameter("application/json", body, ParameterType.RequestBody);
 
            result = _client.Execute(bucketReq);

            if (result.StatusCode == System.Net.HttpStatusCode.Conflict ||
                result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                _bucketFound = true;
            }
            else
            {
                ViewData["Message"] = "Forge bucket can not be accessed !";
                _bucketFound = false;
                return;
            }
        }

        void UploadDrawingFile(String drawingFilePath)
        {
            _fileUrn = String.Empty;

            RestRequest uploadReq = new RestRequest();

            string strFilename = System.IO.Path.GetFileName(drawingFilePath);
            string objectKey = HttpUtility.UrlEncode(strFilename);

            FileStream file = System.IO.File.Open(drawingFilePath, FileMode.Open);
            byte[] fileData = null;
            int nlength = (int)file.Length;
            using (BinaryReader reader = new BinaryReader(file))
            {
                fileData = reader.ReadBytes(nlength);
            }

            uploadReq.Resource = string.Format(ForgeSrv.uploadObjToBucketUrl, _bucketName, objectKey);
            uploadReq.Method = Method.PUT;
            uploadReq.AddParameter("Authorization", "Bearer " + _accessToken, ParameterType.HttpHeader);
            uploadReq.AddParameter("Content-Type", "application/stream");
            uploadReq.AddParameter("Content-Length", nlength);
            uploadReq.AddParameter("requestBody", fileData, ParameterType.RequestBody);

            IRestResponse resp = _client.Execute(uploadReq);

            if (resp.StatusCode == System.Net.HttpStatusCode.OK)
            {
                string responseString = resp.Content;

                int len = responseString.Length;
                string id = "\"objectId\" : \"";
                int index = responseString.IndexOf(id) + id.Length;
                responseString = responseString.Substring(index, len - index - 1);
                int index2 = responseString.IndexOf("\"");
                string urn = responseString.Substring(0, index2);

                byte[] bytes = Encoding.UTF8.GetBytes(urn);
                string urn64 = Convert.ToBase64String(bytes);

                RestRequest bubleReq = new RestRequest();
                bubleReq.Resource = ForgeSrv.transJobUrl;
                bubleReq.Method = Method.POST;
                bubleReq.AddParameter("Authorization", "Bearer " + _accessToken, ParameterType.HttpHeader);
                bubleReq.AddParameter("Content-Type", "application/json;charset=utf-8", ParameterType.HttpHeader);
                bubleReq.AddParameter("x-ads-force", true , ParameterType.HttpHeader);


                string transJobInputParam = "{" +
                                                    "\"input\":" +
                                                    "{" +
                                                         "\"urn\": \""+ urn64 + "\"" +
                                                     "}," +
                                                     " \"output\":" +
                                                     "{ " +
                                                        "\"destination\":" +
                                                         "{" +
                                                             " \"region\": \"us\" " +
                                                          " }, " +
                                                        "\"formats\":" +
                                                             "[" +
                                                                 "{" +
                                                                    "\"type\": \"svf\"," +
                                                                     "\"views\":[\"2d\", \"3d\"]" +
                                                                   "}" +
                                                               "]" +
                                                       "}" +
                                                     "}";

                 bubleReq.AddParameter("application/json", transJobInputParam, ParameterType.RequestBody);

                IRestResponse BubbleResp = _client.Execute(bubleReq);

                String fileId = String.Format("urn:adsk.objects:os.object:{0}/{1}", _bucketName, objectKey);
                byte[] bytes1 = Encoding.UTF8.GetBytes(fileId);
                string urn641 = Convert.ToBase64String(bytes1);

                if (BubbleResp.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    //Translation started
                    _fileUrn = urn64;
                }
                else if (BubbleResp.StatusCode == System.Net.HttpStatusCode.Created)
                {
                    //Translated file already present
                    _fileUrn = urn64;
                }
                else
                {
                    // Error
                    _fileUrn = String.Empty;
                }
            }
        }

        bool CheckProgress()
        {
            bool isComplete = false;

            if (String.IsNullOrEmpty(_fileUrn))
                return false;

            RestRequest statusReq = new RestRequest();
            statusReq.Resource = string.Format(ForgeSrv.transJobStatusUrl, _fileUrn);
            statusReq.Method = Method.GET;
            statusReq.AddParameter("Authorization", "Bearer " + _accessToken, ParameterType.HttpHeader);
            IRestResponse result = _client.Execute(statusReq);

            if (result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                dynamic json = SimpleJson.DeserializeObject(result.Content);
                System.Collections.Generic.Dictionary<string, object>.KeyCollection keys = json.Keys;
                System.Collections.Generic.Dictionary<string, object>.ValueCollection Values = json.Values;

                for (int i = 0; i < Values.Count; i++)
                {
                    var key = keys.ElementAt(i);
                    var item = Values.ElementAt(i);
                    if (key is string && item is string)
                    {
                        if (String.Compare((string)key, "progress") == 0)
                        {
                            String percentComplete = (string)item;
                            if (percentComplete.Contains("complete"))
                            {
                                isComplete = true;
                                break;
                            }
                        }
                    }
                }
            }

            return isComplete;
        }

        public ActionResult Index(Models.ClosetModel cm, String Command)
        {
            ViewBag.Message = " ";

            if (String.IsNullOrEmpty(Command))
            {
                cm.ViewerURN = String.Empty;
                cm.AccessToken = _accessToken;

                return View(cm);
            }

            // Validation
            String message = ValidateInputs(cm);
            if (!String.IsNullOrEmpty(message))
            {
                ViewData["Message"] = message;
                return View(cm);
            }

            _emailAddress = cm.EmailAddress;
            if (Command.Contains("Email"))
            {
                if (String.IsNullOrEmpty(_emailAddress) || !IsValidEmail(_emailAddress))
                {// Invalid email address
                    ViewData["Message"] = "Please provide a valid email address";
                    return View(cm);
                }
            }

            try
            {
                // Create a drawing with closet created based on the user inputs
                String baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

                // Create the closet drawing
                String templateDwgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlankIso.dwg");

                String script = String.Format("CreateCloset{0}{1}{0}{2}{0}{3}{0}{4}{0}{5}{0}{6}{0}{7}{0}_.VSCURRENT{0}sketchy{0}_.Zoom{0}Extents{0}_.SaveAs{0}{0}Result.dwg{0}", Environment.NewLine, _width, _depth, _height, _plyThickness, _doorHeightPercentage, _numberOfDrawers, (_isSplitDrawers == "Yes") ? 1 : 0);

                //make sure the activity has been created by other program!!

                if (Autodesk.AcadIOUtils.UpdateActivity(ForgeSrv.designAuto_Act_Id , script))
                {
                    String resultDrawingPath = String.Empty;

                    // Get the AutoCAD IO result by running "CreateCloset" activity
                    resultDrawingPath = GetAutoCADIOResult(templateDwgPath, ForgeSrv.designAuto_Act_Id);

                    if (!String.IsNullOrEmpty(resultDrawingPath))
                    {
                        // Get a PNG image from the drawing for email attachment

                        //optional: if you want to make snapshot of the new drawing.
                        //_imagePath = GetAutoCADIOResult(resultDrawingPath, "PlotToPNG");
                        //System.IO.File.Copy(_imagePath, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images\\Preview.png"), true);

                        _imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images\\Preview.png");

                        DateTime dt = DateTime.Now;
                        _closetDrawingPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, String.Format("Closet{0}{1}{2}{3}{4}{5}{6}.dwg", dt.Year.ToString(), dt.Month.ToString(), dt.Day.ToString(), dt.Hour.ToString(), dt.Minute.ToString(), dt.Second.ToString(), dt.Millisecond.ToString()));
                        System.IO.File.Copy(resultDrawingPath, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _closetDrawingPath), true);

                        // Send an email with drawing and image as attachments
                        if (Command.Contains("Email"))
                        {
                            if (SendEmail())
                            {
                                ViewData["Message"] = "Email sent !!";
                            }
                            else
                                ViewData["Message"] = "Sorry, Email was not sent !!";
                        }

                        // Preview the drawing in Viewer
                        if (Command.Contains("Preview"))
                        {
                            // Get the urn to show in viewer
                            if (_bucketFound)
                            {
                                UploadDrawingFile(_closetDrawingPath);

                                // Required for the view to reflect changes in the model
                                ModelState.Clear();
                                cm.ViewerURN = _fileUrn;
                                cm.AccessToken = _accessToken;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                // An error !
                ViewData["Message"] = ex.Message;
            }
            //*/

            return View(cm);
        }

        private String ValidateInputs(Models.ClosetModel cm)
        {
            // Validation
            _width = cm.Width;
            if (String.IsNullOrEmpty(_width))
            {// Width is not provided
                return "Please provide a value for the closet width in feet";
            }

            _depth = cm.Depth;
            if (String.IsNullOrEmpty(_depth))
            {// Depth is not provided
                return "Please provide a value for the closet depth in feet";
            }

            _height = cm.Height;
            if (String.IsNullOrEmpty(_height))
            {// Height is not provided
                return "Please provide a value for the closet height in feet";
            }

            _plyThickness = cm.PlyThickness;
            if (String.IsNullOrEmpty(_plyThickness))
            {// Ply Thickness is not provided
                return "Please provide a value for the Ply thickness in inches";
            }

            _doorHeightPercentage = cm.DoorHeightPercentage;
            if (String.IsNullOrEmpty(_doorHeightPercentage))
            {// Door Height is not provided
                return "Please provide a value for the door height as a percentage of total closet height";
            }

            _numberOfDrawers = cm.NumberOfDrawers;
            if (String.IsNullOrEmpty(_numberOfDrawers))
            {// Number of drawers is not provided
                return "Please provide the number of drawers";
            }
            _iNumOfDrawers = 1;
            if (!int.TryParse(_numberOfDrawers, out _iNumOfDrawers))
            {// Invalid entry for number of apps
                return "Please provide the number of drawers";
            }

            _isSplitDrawers = cm.IsSplitDrawers;

            return String.Empty;
        }

        private bool SendEmail()
        {
            try
            {
                //create the mail message
                using (System.Net.Mail.MailMessage mail = new System.Net.Mail.MailMessage())
                {
                    //set the addresses
                    mail.From = new System.Net.Mail.MailAddress(UserSettings.MAIL_USERNAME);
                    mail.To.Add(_emailAddress);

                    //set the content
                    mail.Subject = "Bureau Drawing";
                    mail.Attachments.Add(new System.Net.Mail.Attachment(_closetDrawingPath));

                    //first we create the Plain Text part
                    System.Net.Mail.AlternateView plainView = System.Net.Mail.AlternateView.CreateAlternateViewFromString(String.Format("{0}Width (feet) : {1}{0}Depth (feet) : {2}{0}Height (feet) : {3}{0}Ply Thickness (inches) : {4}{0}Door Height as % of total height: {5}{0}Number of drawers : {6}{0}Is Split drawers ? : {7}{0}"
                        , Environment.NewLine, _width, _depth, _height, _plyThickness, _doorHeightPercentage, _iNumOfDrawers, _isSplitDrawers), null, "text/plain");

                    string desc = String.Format("{0}Width (feet) : {1}{0}<br/>"+ 
                        "Depth (feet) : {2}{0}"+
                        "<br/>Height (feet) : {3}{0}"+
                        "<br/>Ply Thickness (inches) : {4}{0}<br/>"+
                        "Door Height as % of total height: {5}{0}<br/>"+
                        "Number of drawers : {6}{0}<br/>"+
                        "Is Split drawers ? : {7}{0}"
                        , Environment.NewLine, _width, _depth, _height, _plyThickness, _doorHeightPercentage, _iNumOfDrawers, _isSplitDrawers);

                    //then we create the Html part
                    //to embed images, we need to use the prefix 'cid' in the img src value
                    //the cid value will map to the Content-Id of a Linked resource.
                    //thus <img src='cid:companylogo'> will map to a LinkedResource with a ContentId of 'companylogo'
                    System.Net.Mail.AlternateView htmlView = System.Net.Mail.AlternateView.CreateAlternateViewFromString(
                        "<html><body><h3>Here is a preview of the closet. AutoCAD drawing file is attached.</h3><br/><h4>"+desc+"<img src='cid:closetimg'/></body></html>", null, "text/html");

                    if (!String.IsNullOrEmpty(_imagePath))
                    {
                        //create the LinkedResource (embedded image)
                        System.Net.Mail.LinkedResource closetLR = new System.Net.Mail.LinkedResource(_imagePath);
                        closetLR.ContentId = "closetimg";
                        htmlView.LinkedResources.Add(closetLR);
                        System.Net.Mime.ContentType contenttype = new System.Net.Mime.ContentType();
                        contenttype.MediaType = System.Net.Mime.MediaTypeNames.Image.Jpeg;
                        closetLR.ContentType = contenttype;
                    }

                    //add the views
                    //mail.AlternateViews.Add(plainView);
                    mail.AlternateViews.Add(htmlView);

                    //send the message
                    //replace the arguments of SmtpClient with the configuration of your sender email

                    using (System.Net.Mail.SmtpClient smtpClient = 
                        new System.Net.Mail.SmtpClient("smtp-mail.outlook.com", 587))
                    {
                        smtpClient.Credentials = new System.Net.NetworkCredential(UserSettings.MAIL_USERNAME, UserSettings.MAIL_PASSWORD);
                        smtpClient.EnableSsl = true;
                        smtpClient.Send(mail);
                    }
                }
            }
            catch (Exception ex)
            {
                return false;
            }

            return true;
        }

        private bool IsValidEmail(string strIn)
        {
            // Return true if strIn is in valid e-mail format.
            return Regex.IsMatch(strIn, @"^([\w-\.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([\w-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$");
        }

        private String GetAutoCADIOResult(string drawingPath, String activityId)
        {
            String resultPath = String.Empty;
            if (System.IO.File.Exists(drawingPath))
            {
                try
                {
                    // Step 1 : Upload the drawing to S3 storage
                    String hostDwgS3Url = Autodesk.GeneralUtils.UploadDrawingToS3(drawingPath);

                    if (String.IsNullOrEmpty(hostDwgS3Url))
                        return "UploadDrawingToS3 returned empty url";

                    // Step 2 : Submit an AutoCAD IO Workitem using the activity id
                    String resulturl = Autodesk.AcadIOUtils.SubmitWorkItem(activityId, hostDwgS3Url);

                    // Step 3 : Display the result in a web browser and download the result
                    if (String.IsNullOrEmpty(resulturl) == false)
                    {
                        Autodesk.GeneralUtils.Download(resulturl, ref resultPath);
                        if (String.IsNullOrEmpty(resultPath))
                        {
                            resultPath = String.Format("Download resultPath is empty !!");
                        }

                    }
                    else
                        resultPath = "SubmitWorkItem returned empty string";
                }
                catch (System.Exception ex)
                {
                    resultPath = ex.Message;
                }
                finally
                {
                }
            }
            else
                resultPath = String.Format("{0} does not exist !! File.Exists false", drawingPath);

            return resultPath;
        }


    }
}
