using System;
using System.Collections;
using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

using HtmlAgilityPack;
using PlugIn4_5;


namespace GASOSPlugin
{
	/// <summary>
	/// EchoPSV PlugIn for Georgia Secretary of State
	/// </summary>
	public class PlugInClass : IPlugIn
	{
		public override string Fetch(DataRow dr)
		{
            Initialize(dr, true);

            string lic_no = dr["lic_no"].ToString().Replace(".", "").Replace("-", "").Replace("_", "").Replace("/", "").Replace("\\", "").Replace(" ", "").Trim().ToUpper();
            string firstName = dr["dr_fname"].ToString().Trim();
            string lastName = dr["dr_lname"].ToString().Trim();
            string org = dr["orgName"].ToString().Trim();

            if (String.IsNullOrEmpty(org))
            {
                return ErrorMsg.Custom("Invalid organization name");
            }
            if (String.IsNullOrEmpty(lic_no))
            {
                return ErrorMsg.InvalidLicense;
            }
            else if (String.IsNullOrEmpty(firstName) || String.IsNullOrEmpty(lastName))
            {
                return ErrorMsg.InvalidFirstLastName;
            }

			ArrayList profsToQuery = GetProfsToQuery(dr["drtitle"].ToString().ToUpper());

			if (profsToQuery.Count == 0)
			{
                if (org == "Georgia State Board of Optometry") { profsToQuery = GetProfsToQuery("OPT"); }
                else if (org == "Georgia Board of Nursing") { profsToQuery = GetProfsToQuery("RN"); }
                else if (org == "Georgia State Board of Examiners of Psychologists") { profsToQuery = GetProfsToQuery("PSY"); }
                else if (org == "Georgia Board of Chiropractic Examiners") { profsToQuery = GetProfsToQuery("CHIR"); }
                else if (org == "Georgia Board of Marriage and Family Therapists") { profsToQuery = GetProfsToQuery("MFT"); }
                else if (org == "Georgia Board of Professional Counselors") { profsToQuery = GetProfsToQuery("LPC"); }
                else if (org == "Georgia State Board of Podiatry") { profsToQuery = GetProfsToQuery("POD"); }
                else if (org == "Georgia State Board of Physical Therapy") { profsToQuery = GetProfsToQuery("PT"); }
                //else if (org == "Georgia Secretary of State") { profsToQuery = GetProfsToQuery(""); }
                else
                {
                    Match match = Regex.Match(lic_no, "[a-zA-Z]+", RegexOptions.IgnoreCase);

                    if (match.Success)
                    {
                        profsToQuery = GetProfsToQuery(match.Value);

                        if (profsToQuery.Count == 0)
                        {
                            return ErrorMsg.InvalidTitle;
                        }
                    }
                    else
                    {
                        return ErrorMsg.InvalidTitle;
                    }
                }
			}

            HttpWebResponse objResponse = null;
			HttpWebRequest objRequest = null;
            //http://sos.ga.gov/myverification/;
            string basehref = "http://verify.sos.ga.gov/verification/";
			string url = basehref;
            string url2 = basehref;
            string result = String.Empty;
            //MT 04/23/2008 Hit the login page to get the cookies info
            if (WebFetch.IsError(result = WebFetch.ProcessGetRequest2(ref objRequest, ref objResponse, url, "", null, null)))
            {
                return ErrorMsg.CannotAccessSite;
            }                

			result += "\n\r-----------------------------------\n\r";

			foreach(Cookie c in objResponse.Cookies)
			{
				result += c.Name + ":::" + c.Value + ":::" + c.Expires +"\n\r";

			}

            //Use the cookieColl to hold every cookie from every objResponse that comes back
            string pattern = string.Empty;
            string searchStatus = string.Empty;
            CookieCollection cookieColl = new CookieCollection();
            //Add response cookies to cookieCollection
            cookieColl.Add(objResponse.Cookies);
            string theViewState = getFieldVal("__VIEWSTATE", result);
            string eventTarget = getFieldVal("__EVENTTARGET", result);
            string eventArgument = getFieldVal("__EVENTARGUMENT", result);
            // Since some of these potentially need to query against multiple providers, loop through the list and try each
            // For each of them, check whether there were 0, 1 or n results.  If 1 result, then abort loop, otherwise continue through profs
            foreach (string s in profsToQuery)
            {
                string[] profTypeSplit = s.Split('|');
                string professionName = profTypeSplit[0].Replace(" ", "+");
                string licenseTypeName = profTypeSplit[1].Replace(" ", "+");

                string
                    post = "__EVENTTARGET=" + eventTarget
                    + "&__EVENTARGUMENT=" + eventArgument
                    + "&__VIEWSTATE=" + theViewState
                    + "&t_web_lookup__profession_name="  + professionName
                    + "&t_web_lookup__license_type_name=" + licenseTypeName
                    + "&t_web_lookup__first_name=" //+ firstName
                    + "&t_web_lookup__last_name=" //+ lastName
                    + "&t_web_lookup__license_no=" + lic_no
                    + "&t_web_lookup__addr_county="
                    + "&sch_button=Search";

                //url = "http://secure.sos.state.ga.us/myverification/Search.aspx";
                url = "http://verify.sos.ga.gov/verification/";

                if (!WebFetch.IsError(result = WebFetch.ProcessPostRequest(ref objRequest, ref objResponse, url, post, "", null, cookieColl)))
                {
                    
                    result = WebFetch.ProcessPostResponse(ref objResponse, ref objRequest);
                }
                else
                {
                    return ErrorMsg.CannotAccessSearchResultsPage;
                }

                result += "\n\r-----------------------------------\n\r";

                foreach (Cookie c in objResponse.Cookies)
                {
                    result += c.Name + ":::" + c.Value + ":::" + c.Expires + "\n\r";

                }

                // var cookiessss = cookieColl.ToString().Split('/');
                //Add response cookies to cookieCollection
                cookieColl.Add(objResponse.Cookies);
                pattern = "(?<LINK>Details\\.aspx[^\"]*)\"";
                MatchCollection mc = Regex.Matches(result, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (mc.Count < 1)
                {
                    searchStatus = "no matches found";
                }
                else if (mc.Count > 1)
                {
                        searchStatus = "multiple matches found";
                        break;
                }

                else
                {
                        string theLink = mc[0].Groups["LINK"].ToString().Trim();
                        theLink = Regex.Replace(theLink, "&amp;", "&");
                        url = httpPrefix(theLink, basehref);
                        searchStatus = "found";
                        break;
                }
                
            }

            if (searchStatus == "no matches found")
            {
                //return "no matches found";
                return PlugIn4_5.ErrorMsg.NoResultsFound;
            }
			else if (searchStatus == "multiple matches found")
			{
                //return "multiple matches found";
                 return ErrorMsg.MultipleProvidersFound;
            }

			
			if(!WebFetch.IsError(result = WebFetch.ProcessGetRequest2(ref objRequest, ref objResponse, url, "", null, cookieColl))) 
			{
                pdf.Html = "<img id='banner_image' height='121' width='717' src='http://verify.sos.ga.gov/verification/images/banner.png' border='0' /><tr>" + Regex.Match(result, "<td class=\"pagetitle\".*?</div>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline).ToString();
                pdf.Html = Regex.Replace(pdf.Html, "<td class=\".*?colspan=.*?\">", "<td>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                pdf.Html = Regex.Replace(pdf.Html, "Licensee Details", "");
                pdf.Html += "This website is to be used as a primary source verification for licenses issued by the Professional Licensing Boards.  Paper verifications are available for a fee. Please contact the Professional Licensing Boards at 478-207-2440.";
                pdf.ConvertToABCImage(new ImageParameters() { BaseUrl = url });
			}
			else
			{
                return ErrorMsg.CannotAccessDetailsPage;
			}

            result = cleanAllTags(result);
            result = (getLicenseeInfo(result) + "\n" + getLicenseInfo(result) + "\n" + getAssocLics(result) + "\n" + getDiscipInfo(result));
			result = replaceStartTags(result, "br", "\n");
			result = replaceEndTags(result, "br", "");
			result = replaceStartTags(result, "p", "\n");
			result = replaceEndTags(result, "p", "");
			result = replaceStartTags(result, "b", "");
			result = replaceEndTags(result, "b", "");

			Match m = Regex.Match(result, "(No\\s+Discipline\\s+Info)|(No\\s+Other\\s+Documents)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
			
            setSanction(!m.Success);
			
            if (this.expirable)
			{
				handleExpirable(result, dr["lic_exp"].ToString().Trim(), @"Expires\s*(:)*\s*</td>\s*<td>\s*(?<EXP>\d{1,2}(/|-)\d{1,2}(/|-)\d{2,4})\s*</td>", "EXP");
			}
			
			return result;
		}

		protected ArrayList GetProfsToQuery(string title)
		{
			ArrayList profsToQuery = new ArrayList();
			switch(title)
			{
                case "OPT":
                case "O.P.T.":
                {
                    profsToQuery.Add("Optometry|Optometrist");
                    break;
                }
                case "CHIR":
                case "C.H.I.R":
                case "CHIRT":
                case "C.H.I.R.T.":
                {
                    profsToQuery.Add("Chiropractic Examiners|");
                    //profsToQuery.Add("Chiropractic Examiners|Temporary Chiropractor");
                    break;
                }
                
				case "DDS":
				case "D.D.S.":
				case "D.M.D.":
				case "DMD":
				{
					profsToQuery.Add("Dentistry|Conscious Sedation Permit");
					profsToQuery.Add("Dentistry|Dental Faculty");
					profsToQuery.Add("Dentistry|Dentist");
					profsToQuery.Add("Dentistry|Enteral/Inhalation Conscious Sedation");
					profsToQuery.Add("Dentistry|General Anesthesia Permit");
					profsToQuery.Add("Dentistry|Provisional Conscious Sedation Permit");
					profsToQuery.Add("Dentistry|Provisional Dentist");
					profsToQuery.Add("Dentistry|Public Health");
					profsToQuery.Add("Dentistry|Volunteer Dental");
					break;
				}
				case "DH":
				case "D.H.":
				{
					profsToQuery.Add("Dentistry|Dental Hygienist");
					profsToQuery.Add("Dentistry|Provisional Dental Hygienist");
					profsToQuery.Add("Dentistry|Temporary Dental Hygienist");
					break;
				}
				case "LPN":
				case "L.P.N.":
				{
					profsToQuery.Add("Licensed Practical Nurses|");
					break;
				}
				case "MT":
				case "M.T.":
				{
					profsToQuery.Add("Massage Therapy|");
					break;
				}
				case "OT":
				case "O.T.":
				{
					profsToQuery.Add("Occupational Therapy|");
					break;
				}
				case "PHARMD":
				{
					profsToQuery.Add("Pharmacy|");
					break;
				}
				case "PT":
				case "P.T.":
				{
					profsToQuery.Add("Physical Therapy|");
					break;
				}
				case "POD":
				case "P.O.D.":
                case "PODT":
                case "P.O.D.T.":
				{
					profsToQuery.Add("Podiatry|Podiatrist");
                    profsToQuery.Add("Podiatry|Limited Temporary Podiatrist");
					break;
				}
				case "MFT":
				case "M.F.T.":
				{
					profsToQuery.Add("Prof. Coun./Soc. Work/Marriage|Assoc. Marriage and Family Therapy");
					profsToQuery.Add("Prof. Coun./Soc. Work/Marriage|Marriage and Family Therapist");
					break;
				}
				case "APC":
				case "A.P.C.":
				{
					profsToQuery.Add("Prof. Coun./Soc. Work/Marriage|Associate Professional Counselor");
					break;
				}
				case "CSW":
				case "C.S.W.":
				{
					profsToQuery.Add("Prof. Coun./Soc. Work/Marriage|Clinical Social Worker");
					break;
				}
				case "MSW":
				case "M.S.W.":
				{
					profsToQuery.Add("Prof. Coun./Soc. Work/Marriage|Master Social Worker");
					break;
				}
                case "LPC":
                case "L.P.C":
                {
                    profsToQuery.Add("Prof. Coun./Soc. Work/Marriage|Professional Counselor");
                    break;
                }

                case "PSY":
                case "PSYD":
				case "P.S.Y.":
				case "C.D.P.":
				case "CDP":
                case "PS-P":
                case "PS-T":
                case "PSY-V":
				{
					profsToQuery.Add("Psychology|Psychologist");
					profsToQuery.Add("Psychology|Provisional Psychologist");
					profsToQuery.Add("Psychology|Temporary Psychologist");
					profsToQuery.Add("Psychology|Volunteer Psychologist");

					break;
				}
				case "CNM":
				case "C.N.M.":
				{
					profsToQuery.Add("Registered Professional Nurse|Advanced Practice - CNM");
					break;
				}
				case "CNS":
				case "C.N.S.":
                 {
                        profsToQuery.Add("Registered Professional Nurse|Advanced Practice - CNS");
                        break;
                 }
				case "P.M.H.":
				case "PMH":
				{
					profsToQuery.Add("Registered Professional Nurse|Advanced Practice - CNS/PMH");
					break;
				}
				case "CRNA":
				case "C.R.N.A.":
				{
					profsToQuery.Add("Registered Professional Nurse|Advanced Practice - CRNA");
					break;
				}
				case "NP":
				case "N.P.":
				case "A.P.R.N.":
				case "APRN":
				{
					profsToQuery.Add("Registered Professional Nurse|Advanced Practice - NP");
					break;
				}
				case "BN":
				case "B.N.":
				{
					profsToQuery.Add("Registered Professional Nurse|Licensed Undergraduate Nurse");
					break;
				}
				case "R.N.":
				case "RN":
				{
                    profsToQuery.Add("Registered Professional Nurse|Registered Prof Nurse - eNLC");
                    profsToQuery.Add("Registered Professional Nurse|Registered Prof Nurse - Single State");
                    profsToQuery.Add("Registered Professional Nurse|Volunteer Nurse");
                        break;
				}
				case "A.U.D.":
				case "AUD":
				{
					profsToQuery.Add("Speech Pathology %26 Audiology|Audiologist");
					profsToQuery.Add("Speech Pathology %26 Audiology|Audiologist Assistant");
					break;
				}
				case "P.C.E.":
				case "PCE":
				{
					profsToQuery.Add("Speech Pathology %26 Audiology|PCE Temporary License");
					break;
				}
				case "R.P.E.":
				case "RPE":
				{
					profsToQuery.Add("Speech Pathology %26 Audiology|RPE Temporary License");
					break;
				}
				case "S.L.P.":
				case "SLP":
				{
					profsToQuery.Add("Speech Pathology %26 Audiology|Speech Pathologist  Aide");
					profsToQuery.Add("Speech Pathology %26 Audiology|Speech-Language Pathologist");
					break;
				}
			}

			return profsToQuery;
		}

        protected string cleanAllTags(string html)
        {
            var doc = new HtmlDocument();

            doc.LoadHtml(html);
            var tags = doc.DocumentNode.SelectNodes("//*");
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    tag.Attributes.RemoveAll();
                }
            }

            html = doc.DocumentNode.InnerHtml;
            html = Regex.Replace(html, "<span>|</span>|<th>|</th>|<div>|</div>|<h1>|</h1>|<head>|</head>|<script>.*?</script>|<title>.*?</title>|<link>|</link>|<html>|</html>|"+
                    "<form>|</form>|<input>|</input>|<img>|</img>|\\r\\n\\r\\n", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<tr>\\s*?</tr>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<table>\\s*?</table>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return html;
        }
        protected string cleanAllTags2(string html2)
        {
            var doc = new HtmlDocument();

            doc.LoadHtml(html2);
            var tags = doc.DocumentNode.SelectNodes("//*");
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    tag.Attributes.RemoveAll();
                }
            }

            html2 = doc.DocumentNode.InnerHtml;
            html2 = Regex.Replace(html2, "<span>|</span>|<th>|</th>|<div>|</div>|<h1>|</h1>|<head>|</head>|<script>.*?</script>|<title>.*?</title>|<link>|</link>|<html>|</html>|" +
                    "<form>|</form>|<input>|</input>|<img>|</img>|\\r\\n\\r\\n", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html2 = Regex.Replace(html2, "<tr>\\s*?</tr>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html2 = Regex.Replace(html2, "<table>\\s*?</table>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return html2;
        }

        protected string RemoveTags(string html, string tag)
        {
            return Regex.Replace(html, "<" + tag + ".*?>|<\\s*/\\s*" + tag + ">", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

		protected string getLicenseeInfo(string res)
		{
            string pattern = "<table>\\s*?<tr>\\s*?<td>Licensee Details</td>.*?<td>licensee information</td>.*?<td>.*?<table>.*?<tr>(?<nameInfo>.*?)</tr>.*?<table>.*?<tr>(?<address>.*?)</table>";
			Match m = Regex.Match(res, pattern,  RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!m.Success)
                //return "<td>Error:</td><td>Parsing licensee info1</td>";
                return PlugIn4_5.ErrorMsg.CannotAccessDetailsPage;

            res = replaceStartTags(m.Groups["address"].ToString(), "tr", "");
            res = replaceEndTags(res, "tr", "");
            res = Regex.Replace(res, "<td>\\s*?</td>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            Match m2 =  Regex.Match(res,"<td>address:.*?</td>.*?<td>(?<address1>.*?)</td>.*?<td>(?<cityStateZip>.*?)</td>",RegexOptions.Singleline|RegexOptions.IgnoreCase);

            string address = "<td>Address</td>";
            if(!m2.Success){
                address += "<td>Error getting address</td>";
            }
            else
            {
                address += "<td>" + m2.Groups["address1"].ToString() + " " + m2.Groups["cityStateZip"].ToString() + "</td>";
            }
            return m.Groups["nameInfo"].ToString() + address;
		}

		protected string getLicenseInfo(string res)
		{
			res = replaceStartTags(res, "td", "<td>");
			res = replaceEndTags(res, "td", "</td>");
			res = replaceStartTags(res, "tr", "<tr>");
			res = replaceEndTags(res, "tr", "</tr>");
			res = replaceStartTags(res, "table", "<table>");
			res = replaceEndTags(res, "table", "</table>");
			res = replaceStartTags(res, "span", "");
			res = replaceEndTags(res, "span", "");
			res = replaceStartTags(res, "font", "");
			res = replaceEndTags(res, "font", "");

			int licInd = -1;
			Match m = Regex.Match(res, "<td>\\s*lic\\s*#\\s*(:)*\\s*", RegexOptions.Singleline | RegexOptions.IgnoreCase);
			if (m.Success)
				licInd = m.Index;

            if (licInd < 0)
                //return "<td>Error:</td><td>Parsing license info1</td>";
                return PlugIn4_5.ErrorMsg.CannotAccessDetailsPage;

			res = res.Substring(licInd);

			licInd = -1;
			m = Regex.Match(res, "</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
			if (m.Success)
				licInd = m.Index;

			if (licInd < 0)
				//return "<td>Error:</td><td>Parsing license info2</td>";
                return PlugIn4_5.ErrorMsg.CannotAccessDetailsPage;

			res = res.Substring(0, licInd);

			return res;
		}

		protected string getDiscipInfo(string res)
		{
			res = replaceStartTags(res, "td", "<td>");
			res = replaceEndTags(res, "td", "</td>");
			res = replaceStartTags(res, "tr", "");
			res = replaceEndTags(res, "tr", "\n");
			res = replaceStartTags(res, "table", "");
			res = replaceEndTags(res, "table", "\n");
			res = replaceStartTags(res, "span", "");
			res = replaceEndTags(res, "span", "");
			res = replaceStartTags(res, "font", "");
			res = replaceEndTags(res, "font", "");

			int i = 0;
			while((Regex.Match(res, "<td>\\s*<td>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Index > 0) && (i < 100))
			{
				res = Regex.Replace(res, "<td>\\s*<td>", "<td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
				i++;
			}

			i = 0;
			while(Regex.Match(res, "</td>\\s*</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Index > 0 && (i < 100))
			{
				res = Regex.Replace(res, "</td>\\s*</td>", "</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
				i++;
			}
			//return "m.index= " + Regex.Match(res, "<td>\\s*<td>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Index + "::: i= " + i + "\n\n" + res;
			//return res;

			int licInd = -1;
            Match m = Regex.Match(res, "<td>\\s*Public\\s*Board\\s*Orders", RegexOptions.Singleline | RegexOptions.IgnoreCase);
			if (m.Success)
				licInd = m.Index;

            if (licInd < 0)
                //return "<td>Error:</td><td>Parsing discipline info1</td>";
                return ErrorMsg.CannotAccessDetailsPage;

			res = res.Substring(licInd);

			licInd = -1;
            m = Regex.Match(res, "<td>(<br>)*\\s*This\\s*website\\s*is\\s*to\\s*be\\s*used\\s*", RegexOptions.Singleline | RegexOptions.IgnoreCase);
			if (m.Success)
				licInd = m.Index;

			if (licInd < 0)
				//return "<td>Error:</td><td>Parsing discipline info2</td>";
                return ErrorMsg.CannotAccessDetailsPage;

			res = res.Substring(0, licInd);

            //string pattern = "<td><br><br></td>";
            //m = Regex.Match(res, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            //if (m.Success)
            //    return res.Substring(0, m.Index);

			return Regex.Replace(res,"<td><br><br></td>",string.Empty,RegexOptions.IgnoreCase|RegexOptions.Singleline);
		}

		protected string getAssocLics(string res)
		{
			res = replaceStartTags(res, "td", "<td>");
			res = replaceEndTags(res, "td", "</td>");
			res = replaceStartTags(res, "tr", "");
			res = replaceEndTags(res, "tr", "\n");
			res = replaceStartTags(res, "table", "");
			res = replaceEndTags(res, "table", "\n");
			res = replaceStartTags(res, "span", "");
			res = replaceEndTags(res, "span", "");
			res = replaceStartTags(res, "font", "");
			res = replaceEndTags(res, "font", "");

			int i = 0;
			while((Regex.Match(res, "<td>\\s*<td>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Index > 0) && (i < 100))
			{
				res = Regex.Replace(res, "<td>\\s*<td>", "<td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
				i++;
			}

			i = 0;
			while(Regex.Match(res, "</td>\\s*</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Index > 0 && (i < 100))
			{
				res = Regex.Replace(res, "</td>\\s*</td>", "</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
				i++;
			}

			int licInd = -1;
			Match m = Regex.Match(res, "<td>\\s*Associated\\s*", RegexOptions.Singleline | RegexOptions.IgnoreCase);
			if (m.Success)
				licInd = m.Index;

            if (licInd < 0)
                //return "<td>Error:</td><td>Parsing associated licenses info1</td>";
                return PlugIn4_5.ErrorMsg.CannotAcessCredentialPage;

			res = res.Substring(licInd);

			licInd = -1;
            m = Regex.Match(res, "<td>\\s*Public\\s*Board\\s*Orders", RegexOptions.Multiline | RegexOptions.IgnoreCase);
			if (m.Success)
				licInd = m.Index;

			if (licInd < 0)
                //return "<td>Error:</td><td>Parsing associated licenses info2</td>";
                return PlugIn4_5.ErrorMsg.CannotAcessCredentialPage;

            res = res.Substring(0, licInd);
            //res = Regex.Replace(res,"Associated\\s*Licenses", "Associated Licenses</td><td>",RegexOptions.IgnoreCase).ToString();
			return res;
		}



		// Replaces the starting HTML tag (i.e. "<b>", but not "</b>"
		protected string replaceStartTags(string html, string tag, string newvalue)
		{
			if (tag == "!" || tag == "!-" || tag == "!--")
			{
				tag = "!--";
				html = Regex.Replace(html, "<" + tag + ".*?->", newvalue, RegexOptions.Singleline | RegexOptions.IgnoreCase);
			}
			else
				html = Regex.Replace(html, "<" + tag + "(\\s+.*?|)>", newvalue, RegexOptions.Singleline | RegexOptions.IgnoreCase);
			
			return html;
		}

		// Replaces the ending HTML tag (i.e. "</b>", but not "<b>"
		protected string replaceEndTags(string html, string tag, string newvalue)
		{
			html = Regex.Replace(html, "</" + tag + "( .*?|)>", newvalue, RegexOptions.Singleline | RegexOptions.IgnoreCase);
			
			return html;
		}

		// Gets the viewstate and url encodes it
		private string getFieldVal(string fieldName, string html)
		{
			string result;

			// Get the viewstate string out of the html result
			result = Regex.Match(html, fieldName + "\"\\s*value=\"(?<vs>.*?)\"", 
				RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups["vs"].ToString();
			
			// encode the viewstate so it doesn't break the post string
			result = HttpUtility.UrlEncode(result);

			return result;

		}

		protected string httpPrefix(string theLoc, string prefix)
		{
			string retLoc = theLoc;
			//See if start with "http://" or "https://"
			string pattern = "(^(http|https))://(?<LOCATION>.*?)('|\"|>| |	|$)";
			Match m1 = Regex.Match(theLoc, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
			//Did not find, so prefix with the http/https:// that was passed-in
			if (!m1.Success)
			{
				if ( theLoc.StartsWith("/") && prefix.EndsWith("/"))
					theLoc = theLoc.Substring(1);
				if ( !theLoc.StartsWith("/") && !prefix.EndsWith("/"))
					theLoc = "/" + theLoc;

				retLoc = prefix + theLoc;
			}

			return retLoc;
		}

		protected void handleExpirable(string result, string dr_lic_exp, string patrn_lic_exp, string expGrpName)
		{
			//Match m = Regex.Match("(?si)Discipline( |\t|\r|\v|\f|\n|(&nbsp;))*:</td>( |\t|\r|\v|\f|\n|(&nbsp;))*<td>( |\t|\r|\v|\f|\n|(&nbsp;))*(?<EXPDT>.*?)( |\t|\r|\v|\f|\n|(&nbsp;))*</td>");
			Match m = Regex.Match(result,patrn_lic_exp,  RegexOptions.IgnoreCase | RegexOptions.Singleline);

			if (m.Success)
			{
				DateTime expiration;

				try
				{
					expiration = DateTime.Parse(m.Groups[expGrpName].ToString().Trim());
					try
					{
						DateTime dr_lic_expDT = DateTime.Parse(dr_lic_exp);
						checkExpirable(dr_lic_expDT, expiration);
					}
					catch(Exception e1)
					{	//fromClient dateTime is blank/invalid, so send some really old fake value in its place
						checkExpirable((new DateTime(1492, 1, 1)), expiration);
					}
				}
				catch(Exception e2) //fromSite dateTime is blank/invalid; do nothing
				{
					string msg = e2.Message;
				}
				
			}
            //if the expiration date cant be parsed then send expirable back
            else
            {
                checkExpirable((new DateTime(1492, 1, 1)), (new DateTime(1493, 1, 1)));
            }
		}

	}
}
