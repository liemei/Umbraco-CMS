using System;
using System.Xml;
using System.Web.Caching;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

using System.Data;
using System.Web.UI;
using System.Collections;
using System.Collections.Generic;
using Umbraco.Core;
using Umbraco.Core.Cache;
using Umbraco.Core.Configuration;
using Umbraco.Web;
using Umbraco.Web.Cache;
using umbraco.DataLayer;
using umbraco.BusinessLogic;
using Umbraco.Core.IO;
using System.Web;
using Umbraco.Core.Xml;
using Umbraco.Web.Composing;
using Umbraco.Web.Macros;

namespace umbraco
{
    /// <summary>
    /// Holds methods for parsing and building umbraco templates
    /// </summary>
    [Obsolete("Do not use this class, use Umbraco.Core.Service.IFileService to work with templates")]
    public class template
    {
        #region private variables

        readonly StringBuilder _templateOutput = new StringBuilder();

        private string _templateDesign = "";
        int _masterTemplate = -1;
        private string _templateName = "";
        private string _templateAlias = "";

        #endregion

        #region public properties
        public String TemplateContent
        {
            set
            {
                _templateOutput.Append(value);
            }
            get
            {
                return _templateOutput.ToString();
            }
        }

        public int MasterTemplate
        {
            get { return _masterTemplate; }
        }

        //added fallback to the default template to avoid nasty .net errors.
        //This is referenced in /default.aspx.cs during page rendering.
        public string MasterPageFile
        {
            get
            {

                string file = TemplateAlias.Replace(" ", "") + ".master";
                string path = SystemDirectories.Masterpages + "/" + file;


                if (System.IO.File.Exists(IOHelper.MapPath(VirtualPathUtility.ToAbsolute(path))))
                    return path;
                else
                    return SystemDirectories.Umbraco + "/masterPages/default.master";
            }
        }

        //Support for template folders, if a alternative skin folder is requested
        //we will try to look for template files in another folder
        public string AlternateMasterPageFile(string templateFolder)
        {
            string file = TemplateAlias.Replace(" ", "") + ".master";
            string path = SystemDirectories.Masterpages + "/" + templateFolder + "/" + file;

            //if it doesn't exists then we return the normal file
            if (!System.IO.File.Exists(IOHelper.MapPath(VirtualPathUtility.ToAbsolute(path))))
            {

                string originalPath = IOHelper.MapPath(VirtualPathUtility.ToAbsolute(MasterPageFile));
                string copyPath = IOHelper.MapPath(VirtualPathUtility.ToAbsolute(path));

                string newFile;
                using (var fs = new FileStream(originalPath, FileMode.Open, FileAccess.ReadWrite))
                using (var f = new StreamReader(fs))
                {
                    newFile = f.ReadToEnd();
                }

                newFile = newFile.Replace("MasterPageFile=\"~/masterpages/", "MasterPageFile=\"");

                using (var fs = new FileStream(copyPath, FileMode.Create, FileAccess.Write))
                using (var replacement = new StreamWriter(fs))
                {
                    replacement.Write(newFile);
                }
            }

            return path;

        }


        public string TemplateAlias
        {
            get { return _templateAlias; }
        }
        #endregion

        #region public methods

        public override string ToString()
        {
            return this._templateName;
        }

        public Control ParseWithControls(page umbPage)
        {
            System.Web.HttpContext.Current.Trace.Write("umbracoTemplate", "Start parsing");

            if (System.Web.HttpContext.Current.Items["macrosAdded"] == null)
                System.Web.HttpContext.Current.Items.Add("macrosAdded", 0);

            StringBuilder tempOutput = _templateOutput;

            Control pageLayout = new Control();
            Control pageHeader = new Control();
            Control pageFooter = new Control();
            Control pageContent = new Control();
            System.Web.UI.HtmlControls.HtmlForm pageForm = new System.Web.UI.HtmlControls.HtmlForm();
            System.Web.UI.HtmlControls.HtmlHead pageAspNetHead = new System.Web.UI.HtmlControls.HtmlHead();

            // Find header and footer of page if there is an aspnet-form on page
            if (_templateOutput.ToString().ToLower().IndexOf("<?aspnet_form>") > 0 ||
                _templateOutput.ToString().ToLower().IndexOf("<?aspnet_form disablescriptmanager=\"true\">") > 0)
            {
                pageForm.Attributes.Add("method", "post");
                pageForm.Attributes.Add("action", Convert.ToString(System.Web.HttpContext.Current.Items["VirtualUrl"]));

                // Find header and footer from tempOutput
                int aspnetFormTagBegin = tempOutput.ToString().ToLower().IndexOf("<?aspnet_form>");
                int aspnetFormTagLength = 14;
                int aspnetFormTagEnd = tempOutput.ToString().ToLower().IndexOf("</?aspnet_form>") + 15;

                // check if we should disable the script manager
                if (aspnetFormTagBegin == -1)
                {
                    aspnetFormTagBegin =
                        _templateOutput.ToString().ToLower().IndexOf("<?aspnet_form disablescriptmanager=\"true\">");
                    aspnetFormTagLength = 42;
                }
                else
                {
                    ScriptManager sm = new ScriptManager();
                    sm.ID = "umbracoScriptManager";
                    pageForm.Controls.Add(sm);
                }


                StringBuilder header = new StringBuilder(tempOutput.ToString().Substring(0, aspnetFormTagBegin));

                // Check if there's an asp.net head element in the header
                if (header.ToString().ToLower().Contains("<?aspnet_head>"))
                {
                    StringBuilder beforeHeader = new StringBuilder(header.ToString().Substring(0, header.ToString().ToLower().IndexOf("<?aspnet_head>")));
                    header.Remove(0, header.ToString().ToLower().IndexOf("<?aspnet_head>") + 14);
                    StringBuilder afterHeader = new StringBuilder(header.ToString().Substring(header.ToString().ToLower().IndexOf("</?aspnet_head>") + 15, header.Length - header.ToString().ToLower().IndexOf("</?aspnet_head>") - 15));
                    header.Remove(header.ToString().ToLower().IndexOf("</?aspnet_head>"), header.Length - header.ToString().ToLower().IndexOf("</?aspnet_head>"));

                    // Find the title from head
                    MatchCollection matches = Regex.Matches(header.ToString(), @"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if (matches.Count > 0)
                    {
                        StringBuilder titleText = new StringBuilder();
                        HtmlTextWriter titleTextTw = new HtmlTextWriter(new System.IO.StringWriter(titleText));
                        parseStringBuilder(new StringBuilder(matches[0].Groups[1].Value), umbPage).RenderControl(titleTextTw);
                        pageAspNetHead.Title = titleText.ToString();
                        header = new StringBuilder(header.ToString().Replace(matches[0].Value, ""));
                    }

                    pageAspNetHead.Controls.Add(parseStringBuilder(header, umbPage));
                    pageAspNetHead.ID = "head1";

                    // build the whole header part
                    pageHeader.Controls.Add(parseStringBuilder(beforeHeader, umbPage));
                    pageHeader.Controls.Add(pageAspNetHead);
                    pageHeader.Controls.Add(parseStringBuilder(afterHeader, umbPage));

                }
                else
                    pageHeader.Controls.Add(parseStringBuilder(header, umbPage));


                pageFooter.Controls.Add(parseStringBuilder(new StringBuilder(tempOutput.ToString().Substring(aspnetFormTagEnd, tempOutput.Length - aspnetFormTagEnd)), umbPage));
                tempOutput.Remove(0, aspnetFormTagBegin + aspnetFormTagLength);
                aspnetFormTagEnd = tempOutput.ToString().ToLower().IndexOf("</?aspnet_form>");
                tempOutput.Remove(aspnetFormTagEnd, tempOutput.Length - aspnetFormTagEnd);


                //throw new ArgumentException(tempOutput.ToString());
                pageForm.Controls.Add(parseStringBuilder(tempOutput, umbPage));

                pageContent.Controls.Add(pageHeader);
                pageContent.Controls.Add(pageForm);
                pageContent.Controls.Add(pageFooter);
                return pageContent;

            }
            else
                return parseStringBuilder(tempOutput, umbPage);

        }

        public Control parseStringBuilder(StringBuilder tempOutput, page umbPage)
        {

            Control pageContent = new Control();

            bool stop = false;
            bool debugMode = UmbracoContext.Current.HttpContext.IsDebuggingEnabled;

            while (!stop)
            {
                System.Web.HttpContext.Current.Trace.Write("template", "Begining of parsing rutine...");
                int tagIndex = tempOutput.ToString().ToLower().IndexOf("<?umbraco");
                if (tagIndex > -1)
                {
                    String tempElementContent = "";
                    pageContent.Controls.Add(new LiteralControl(tempOutput.ToString().Substring(0, tagIndex)));

                    tempOutput.Remove(0, tagIndex);

                    String tag = tempOutput.ToString().Substring(0, tempOutput.ToString().IndexOf(">") + 1);
                    Hashtable attributes = new Hashtable(XmlHelper.GetAttributesFromElement(tag));

                    // Check whether it's a single tag (<?.../>) or a tag with children (<?..>...</?...>)
                    if (tag.Substring(tag.Length - 2, 1) != "/" && tag.IndexOf(" ") > -1)
                    {
                        String closingTag = "</" + (tag.Substring(1, tag.IndexOf(" ") - 1)) + ">";
                        // Tag with children are only used when a macro is inserted by the umbraco-editor, in the
                        // following format: "<?UMBRACO_MACRO ...><IMG SRC="..."..></?UMBRACO_MACRO>", so we
                        // need to delete extra information inserted which is the image-tag and the closing
                        // umbraco_macro tag
                        if (tempOutput.ToString().IndexOf(closingTag) > -1)
                        {
                            tempOutput.Remove(0, tempOutput.ToString().IndexOf(closingTag));
                        }
                    }



                    System.Web.HttpContext.Current.Trace.Write("umbTemplate", "Outputting item: " + tag);

                    // Handle umbraco macro tags
                    if (tag.ToString().ToLower().IndexOf("umbraco_macro") > -1)
                    {
                        if (debugMode)
                            pageContent.Controls.Add(new LiteralControl("<div title=\"Macro Tag: '" + System.Web.HttpContext.Current.Server.HtmlEncode(tag) + "'\" style=\"border: 1px solid #009;\">"));

                        // NH: Switching to custom controls for macros
                        if (UmbracoConfig.For.UmbracoSettings().Templates.UseAspNetMasterPages)
                        {
                            umbraco.presentation.templateControls.Macro macroControl = new umbraco.presentation.templateControls.Macro();
                            macroControl.Alias = helper.FindAttribute(attributes, "macroalias");
                            IDictionaryEnumerator ide = attributes.GetEnumerator();
                            while (ide.MoveNext())
                                if (macroControl.Attributes[ide.Key.ToString()] == null)
                                    macroControl.Attributes.Add(ide.Key.ToString(), ide.Value.ToString());
                            pageContent.Controls.Add(macroControl);
                        }
                        else
                        {
                            var macroId = helper.FindAttribute(attributes, "macroid");
                            if (macroId == string.Empty) macroId = helper.FindAttribute(attributes, "macroalias");
                            var tempMacro = GetMacro(macroId);
                            if (tempMacro != null)
                            {

                                try
                                {
                                    var renderer = new MacroRenderer(Current.ProfilingLogger);
                                    var c = renderer.Render(tempMacro, umbPage.Elements, umbPage.PageID, attributes).GetAsControl();
                                    if (c != null)
                                        pageContent.Controls.Add(c);
                                    else
                                        System.Web.HttpContext.Current.Trace.Warn("Template", "Result of macro " + tempMacro.Name + " is null");

                                }
                                catch (Exception e)
                                {
                                    System.Web.HttpContext.Current.Trace.Warn("Template", "Error adding macro " + tempMacro.Name, e);
                                }
                            }
                        }
                        if (debugMode)
                            pageContent.Controls.Add(new LiteralControl("</div>"));
                    }
                    else
                    {
                        if (tag.ToLower().IndexOf("umbraco_getitem") > -1)
                        {

                            // NH: Switching to custom controls for items
                            if (UmbracoConfig.For.UmbracoSettings().Templates.UseAspNetMasterPages)
                            {
                                umbraco.presentation.templateControls.Item itemControl = new umbraco.presentation.templateControls.Item();
                                itemControl.Field = helper.FindAttribute(attributes, "field");
                                IDictionaryEnumerator ide = attributes.GetEnumerator();
                                while (ide.MoveNext())
                                    if (itemControl.Attributes[ide.Key.ToString()] == null)
                                        itemControl.Attributes.Add(ide.Key.ToString(), ide.Value.ToString());
                                pageContent.Controls.Add(itemControl);
                            }
                            else
                            {
                                try
                                {
                                    //TODO: Make this work again - but let's make sure this whole class is overhauled and useful

                                    //if (helper.FindAttribute(attributes, "nodeId") != "" && int.Parse(helper.FindAttribute(attributes, "nodeId")) != 0)
                                    //{
                                    //    cms.businesslogic.Content c = new umbraco.cms.businesslogic.Content(int.Parse(helper.FindAttribute(attributes, "nodeId")));
                                    //    item umbItem = new item(c.getProperty(helper.FindAttribute(attributes, "field")).Value.ToString(), attributes);
                                    //    tempElementContent = umbItem.FieldContent;

                                    //    // Check if the content is published
                                    //    if (c.nodeObjectType == cms.businesslogic.web.Document._objectType)
                                    //    {
                                    //        try
                                    //        {
                                    //            cms.businesslogic.web.Document d = (cms.businesslogic.web.Document)c;
                                    //            if (!d.Published)
                                    //                tempElementContent = "";
                                    //        }
                                    //        catch { }
                                    //    }

                                    //}
                                    //else
                                    //{
                                    //    // NH adds Live Editing test stuff
                                    //    item umbItem = new item(umbPage.Elements, attributes);
                                    //    //								item umbItem = new item(umbPage.PageElements[helper.FindAttribute(attributes, "field")].ToString(), attributes);
                                    //    tempElementContent = umbItem.FieldContent;
                                    //}

                                    if (debugMode)
                                        tempElementContent =
                                            "<div title=\"Field Tag: '" + System.Web.HttpContext.Current.Server.HtmlEncode(tag) + "'\" style=\"border: 1px solid #fc6;\">" + tempElementContent + "</div>";
                                }
                                catch (Exception e)
                                {
                                    System.Web.HttpContext.Current.Trace.Warn("umbracoTemplate", "Error reading element (" + helper.FindAttribute(attributes, "field") + ")", e);
                                }
                            }
                        }
                    }
                    tempOutput.Remove(0, tempOutput.ToString().IndexOf(">") + 1);
                    tempOutput.Insert(0, tempElementContent);
                }
                else
                {
                    pageContent.Controls.Add(new LiteralControl(tempOutput.ToString()));
                    break;
                }

            }

            return pageContent;

        }


        /// <summary>
        /// Parses the content of the templateOutput stringbuilder, and matches any tags given in the
        /// XML-file /umbraco/config/umbracoTemplateTags.xml.
        /// Replaces the found tags in the StringBuilder object, with "real content"
        /// </summary>
        /// <param name="umbPage"></param>
        public void Parse(page umbPage)
        {
            System.Web.HttpContext.Current.Trace.Write("umbracoTemplate", "Start parsing");

            // First parse for known umbraco tags
            // <?UMBRACO_MACRO/> - macros
            // <?UMBRACO_GETITEM/> - print item from page, level, or recursive
            MatchCollection tags = Regex.Matches(_templateOutput.ToString(), "<\\?UMBRACO_MACRO[^>]*/>|<\\?UMBRACO_GETITEM[^>]*/>|<\\?(?<tagName>[\\S]*)[^>]*/>", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

            foreach (Match tag in tags)
            {
                Hashtable attributes = new Hashtable(XmlHelper.GetAttributesFromElement(tag.Value));


                if (tag.ToString().ToLower().IndexOf("umbraco_macro") > -1)
                {
                    var macroId = helper.FindAttribute(attributes, "macroid");
                    if (macroId != "")
                        _templateOutput.Replace(tag.Value, string.Empty);
                }
                else
                {
                    if (tag.ToString().ToLower().IndexOf("umbraco_getitem") > -1)
                    {
                        try
                        {
                            var tempElementContent = umbPage.Elements[helper.FindAttribute(attributes, "field")].ToString();
                            var tempMacros = Regex.Matches(tempElementContent, "<\\?UMBRACO_MACRO(?<attributes>[^>]*)><img[^>]*><\\/\\?UMBRACO_MACRO>", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
                            foreach (Match tempMacro in tempMacros)
                            {
                                var tempAttributes = new Hashtable(XmlHelper.GetAttributesFromElement(tempMacro.Groups["attributes"].Value));
                                var macroId = helper.FindAttribute(tempAttributes, "macroid");
                                if (Convert.ToInt32(macroId) > 0)
                                    _templateOutput.Replace(tag.Value, string.Empty);
                            }

                            _templateOutput.Replace(tag.Value, tempElementContent);
                        }
                        catch (Exception e)
                        {
                            System.Web.HttpContext.Current.Trace.Warn("umbracoTemplate", "Error reading element (" + helper.FindAttribute(attributes, "field") + ")", e);
                        }
                    }
                }
            }

            System.Web.HttpContext.Current.Trace.Write("umbracoTemplate", "Done parsing");
        }



        #endregion

        #region private methods

        private static MacroModel GetMacro(string macroId)
        {
            HttpContext.Current.Trace.Write("umbracoTemplate", "Starting macro (" + macroId + ")");
            // it's all obsolete anyways...
            var macro = Current.Services.MacroService.GetByAlias(macroId);
            return macro == null ? null : new MacroModel(macro);
        }

        #endregion

        #region constructors

        public static string GetMasterPageName(int templateID)
        {
            return GetMasterPageName(templateID, null);
        }

        public static string GetMasterPageName(int templateID, string templateFolder)
        {
            var t = new template(templateID);

            return !string.IsNullOrEmpty(templateFolder)
                ? t.AlternateMasterPageFile(templateFolder)
                : t.MasterPageFile;
        }

        public template(int templateID)
        {
            var tId = templateID;

            var t = Current.ApplicationCache.RuntimeCache.GetCacheItem<template>(
               string.Format("{0}{1}", CacheKeys.TemplateFrontEndCacheKey, tId), () =>
               {
                   dynamic templateData;
                   using (var scope = Current.ScopeProvider.CreateScope())
                   {
                       templateData = scope.Database.FirstOrDefault<dynamic>(
                           @"select nodeId, alias, node.parentID as master, text, design
from cmsTemplate
inner join umbracoNode node on (node.id = cmsTemplate.nodeId)
where nodeId = @templateID",
                           new { templateID = templateID });
                   }
                   if (templateData != null)
                   {
                       // Get template master and replace content where the template
                       if (templateData.master != null)
                           _masterTemplate = templateData.master;
                       if (templateData.alias != null)
                           _templateAlias = templateData.alias;
                       if (templateData.text != null)
                           _templateName = templateData.text;
                       if (templateData.design != null)
                           _templateDesign = templateData.design;
                   }

                   return this;
               });

            if (t == null)
                throw new InvalidOperationException("Could not find a tempalte with id " + templateID);

            this._masterTemplate = t._masterTemplate;
            this._templateAlias = t._templateAlias;
            this._templateDesign = t._templateDesign;
            this._masterTemplate = t._masterTemplate;
            this._templateName = t._templateName;

            // Only check for master on legacy templates - can show error when using master pages.
            if (!UmbracoConfig.For.UmbracoSettings().Templates.UseAspNetMasterPages)
            {
                checkForMaster(tId);
            }

        }

        private void checkForMaster(int templateID) {
            // Get template design
            if (_masterTemplate != 0 && _masterTemplate != templateID) {
                template masterTemplateDesign = new template(_masterTemplate);
                if (masterTemplateDesign.TemplateContent.IndexOf("<?UMBRACO_TEMPLATE_LOAD_CHILD/>") > -1
                    || masterTemplateDesign.TemplateContent.IndexOf("<?UMBRACO_TEMPLATE_LOAD_CHILD />") > -1) {
                    _templateOutput.Append(
                        masterTemplateDesign.TemplateContent.Replace("<?UMBRACO_TEMPLATE_LOAD_CHILD/>",
                        _templateDesign).Replace("<?UMBRACO_TEMPLATE_LOAD_CHILD />", _templateDesign)
                        );
                } else
                    _templateOutput.Append(_templateDesign);
            } else {
                if (_masterTemplate == templateID)
                {
                    cms.businesslogic.template.Template t = cms.businesslogic.template.Template.GetTemplate(templateID);
                    string templateName = (t != null) ? t.Text : string.Format("'Template with id: '{0}", templateID);
                    System.Web.HttpContext.Current.Trace.Warn("template",
                        String.Format("Master template is the same as the current template. It would cause an endless loop! Make sure that the current template '{0}' has another Master Template than itself. You can change this in the template editor under 'Settings'", templateName));
                    _templateOutput.Append(_templateDesign);
                }
            }
        }

        [Obsolete("Use ApplicationContext.Current.ApplicationCache.ClearCacheForTemplate instead")]
        public static void ClearCachedTemplate(int templateID)
        {
            DistributedCache.Instance.RefreshTemplateCache(templateID);
        }

        public template(String templateContent)
        {
            _templateOutput.Append(templateContent);
            _masterTemplate = 0;
        }

        #endregion
    }
}
