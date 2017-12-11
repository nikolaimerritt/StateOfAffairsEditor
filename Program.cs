using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using Renci.SshNet;

namespace StateOfAffairsEditor
{    
    class Program
    {
        const string FILE_NAME = "ArticlePlan.txt";

        static int Main(string[] args)
        {
            string articleTitleToDelete = getArticleToDelete();
            if (articleTitleToDelete != "") { Console.WriteLine($"|{articleTitleToDelete}|");  deleteArticle(articleTitleToDelete); }
            else
            {
                string articleTitle = "";
                uploadArticle(articleWebpageFromFile(out articleTitle), articleTitle);                
            }
            return 0;
        }
        
        static string articleWebpageFromFile(out string articleTitle)
        {
            StreamReader file;
            const string COMMAND_DELIM = " -> ";
            bool error = false;
            string html = "";
            articleTitle = "article";

            do
            {
                string line;
                string headHTML = "<head> \n <link rel='stylesheet' type='text/css' href='ArticleStyle.css'> \n";
                string bodyHTML = "<body> \n\t <div class='article'> \n ";
                const string ARG_PLACEHOLDER = "$ARG_PLACEHOLDER$";
                const string BANNER_PLACEHOLDER = "https://www.globalpartnership.org/sites/default/files/styles/1400x640/public/uk-banner-desktop_0.jpg?itok=UOYcw6ts";
                var commandToHTML = new Dictionary<string, string>
                {
                    {"title", $@"<div class='wrapper' style='background: url({BANNER_PLACEHOLDER}) no-repeat center center fixed; background-size: cover;'>
    <div class='wrapperText'>
      <h1>
        <mark style='font-size: 50px'> {ARG_PLACEHOLDER} </mark>
      </h1>
      <p id='author'>
        <mark> run by Emma Churms</mark>
      </p>      
    </div>
  </div>
<div class='articleText'>" },
                    {"heading", $@"<h2> {ARG_PLACEHOLDER} </h2>"},
                    {"p", $@"<p> {ARG_PLACEHOLDER} </p>"},
                    {"bold-section", $@"<h4 class='introduction'> {ARG_PLACEHOLDER} </h4>"}
                };

                try
                {
                    file = new StreamReader(FILE_NAME);
                    while ((line = file.ReadLine()) != null)
                    {                        
                        string command, argument;

                        if (line.Contains(COMMAND_DELIM))
                        {
                            var lineSplit = line.Split(new[] { COMMAND_DELIM }, StringSplitOptions.None);
                            command = lineSplit[0].ToLower().Replace(" ", "");
                            argument = lineSplit[1];
                        }
                        else // paragraph by default
                        {
                            command = "p";
                            argument = line;
                        }
                        
                        if (command == "title")
                        {
                            headHTML += $"<title> {argument} </title>";
                            articleTitle = argument;
                        }
                        else if (command == "banner") { bodyHTML = bodyHTML.Replace(BANNER_PLACEHOLDER, argument); }
                        argument = useHTMLEntities(argument);
                        if (commandToHTML.ContainsKey(command)) { bodyHTML += commandToHTML[command].Replace(ARG_PLACEHOLDER, argument) + "\n"; }
                    }

                    bodyHTML += "</div> \n </div> \n </body> \n";
                    headHTML += "</head> \n";
                    html = "<html> \n" + headHTML + bodyHTML + "</html>";
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine("\n======== ERROR ========\n");
                    Console.WriteLine($"'{FILE_NAME}' could not be found. \nPlease either create a new text file here (Right click, New, Text Document) called '{FILE_NAME}', or if you know you've moved the file, put it back here. \nPress Enter once you've done so, and I'll try again.");
                    Console.ReadLine();
                    error = true;
                }
            } while (error);

            return html;
        }

        static void uploadArticle(string articleHTML, string articleTitle)
        {
            var connInfo = new ConnectionInfo("www.stateofaffairs.duckdns.org", 22, "pi", new AuthenticationMethod[] {
                new PasswordAuthenticationMethod("pi", Encoding.ASCII.GetBytes("enumaEli_s"))
            });
            using (var ssh = new SshClient(connInfo))
            {
                ssh.Connect();

                // uploading article
                string articleFileName = fileNameFromArticleTitle(articleTitle);
                using (var cmd = ssh.CreateCommand($"echo \"{articleHTML}\" > /var/www/html/{articleFileName}")) { cmd.Execute(); }

                // updating index.html to point to article
                string indexHTML = "";                
                using (var cmd = ssh.CreateCommand($"cat /var/www/html/index.html")) { indexHTML = cmd.Execute().Replace('\"', '\''); }

                string htmlToInsert = $"\n\t <a href='{articleFileName}' class='w3-bar-item w3-button'> {articleTitle} </a>";                
                const string lineBefore = "<!-- Articles Here -->";                
                if (!indexHTML.Contains(htmlToInsert))
                {
                    indexHTML = indexHTML.Replace(lineBefore, lineBefore + htmlToInsert);
                    using (var cmd = ssh.CreateCommand($"echo \"{indexHTML}\" > /var/www/html/index.html")) { cmd.Execute(); }
                }                
                ssh.Disconnect();
            }
        }

        static string getArticleToDelete()
        {
            string fileContents = new StreamReader(FILE_NAME).ReadToEnd();
            if (fileContents.Contains("remove article ")) { return fileContents.Replace("remove article ", ""); }
            return "";
        }

        static void deleteArticle(string articleTitle)
        {
            var connInfo = new ConnectionInfo("www.stateofaffairs.duckdns.org", 22, "pi", new AuthenticationMethod[] {
                new PasswordAuthenticationMethod("pi", Encoding.ASCII.GetBytes("enumaEli_s"))
            });
            using (var ssh = new SshClient(connInfo))
            {
                ssh.Connect();

                // deleting article file
                string articleFile = fileNameFromArticleTitle(articleTitle);
                using (var cmd = ssh.CreateCommand($"rm /var/www/html/{articleFile}")) { cmd.Execute(); }

                // updating index.html to not point to article
                string indexHTML = "";
                using (var cmd = ssh.CreateCommand($"cat /var/www/html/index.html")) { indexHTML = cmd.Execute().Replace('\"', '\''); }
                string htmlToRemove = $"<a href='{articleFile}' class='w3-bar-item w3-button'> {articleTitle} </a>";
                Console.WriteLine($"|{htmlToRemove}|");
                if (indexHTML.Contains(htmlToRemove))
                {
                    indexHTML = indexHTML.Replace(htmlToRemove, "");
                    using (var cmd = ssh.CreateCommand($"echo \"{indexHTML}\" > /var/www/html/index.html")) { cmd.Execute(); }
                }
                ssh.Disconnect();
            }
        }

        static string useHTMLEntities(string originalHTML) { return originalHTML.Replace("\"", "&amp;").Replace("\'", "&apos;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("£", "&pound;").Replace("-", "&ndash;"); }
        static string fileNameFromArticleTitle(string articleTitle) { return articleTitle.Trim().Replace(" ", "-").ToLower() + ".html"; }
    }
}
