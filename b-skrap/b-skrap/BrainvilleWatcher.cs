using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace b_skrap
{
    public class BrainvilleWatcher
    {
        private static IPlaywright? _pw;
        private static IBrowser? _browser;

        private const string Url =
            "https://www.brainville.com/HittaKonsultuppdrag?Text=frontend+angular+react+vue";

        // Ändra till "0 */20 * * * *" för var 20:e minut när du är klar att köra i Azure.
        [Function("BrainvilleWatcher")]
        public async Task Run([TimerTrigger("0 */20 * * * *")] TimerInfo _, FunctionContext ctx)
        {
            var log = ctx.GetLogger("BrainvilleWatcher");

            // Init headless Chromium
            _pw ??= await Playwright.CreateAsync();
            _browser ??= await _pw.Chromium.LaunchAsync(new() { Headless = true });

            var context = await _browser.NewContextAsync(new()
            {
                Locale = "sv-SE",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
            });

            var page = await context.NewPageAsync();
            await page.GotoAsync(Url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Försök stänga cookie-banner (om den finns)
            try
            {
                await page.Locator("button:has-text('Acceptera'), button:has-text('Godkänn'), #onetrust-accept-btn-handler")
                          .First.ClickAsync(new() { Timeout = 2000 });
                await page.WaitForTimeoutAsync(500);
            }
            catch { /* ok om inte finns */ }

            // Låt sidan rendera listan
            await page.WaitForTimeoutAsync(1500);

            // Plocka jobbkortens länkar + titlar
            //var anchors = await page.QuerySelectorAllAsync("div.c_card a[href*='/PublicPage/Requisition/']");
            //var anchorsTest = await page.QuerySelectorAllAsync("div.c_card");
            // Vänta in korten
            await page.WaitForSelectorAsync("div.c_card", new() { Timeout = 30000 });
            // liten paus för render
            await page.WaitForTimeoutAsync(400);

            // Hämta alla kort
            var cards = page.Locator("div.c_card");
            int cardCount = await cards.CountAsync();

            var results = new List<(string Id, string Title, string Link)>();
            for (int i = 0; i < cardCount; i++)
            {
                var card = cards.Nth(i);

                // Primär selektor: titellänken
                var a = card.Locator("a[data-font-type='title']").First;
                if (await a.CountAsync() == 0)
                    a = card.Locator("a[href*='/PublicPage/Requisition/']").First; // fallback

                if (await a.CountAsync() == 0) continue;

                var title = (await a.InnerTextAsync())?.Trim();
                var href = (await a.GetAttributeAsync("href"))?.Trim();
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href)) continue;

                if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    href = "https://www.brainville.com" + href;

                // Försök få ut ett ID ur länken (siffror efter /Requisition/)
                var idMatch = System.Text.RegularExpressions.Regex.Match(href, @"Requisition/(\d+)");
                var id = idMatch.Success ? idMatch.Groups[1].Value : href; // fallback till hela länken

                results.Add((id, title, href));
            }

            log.LogInformation("Kort hittade: {cards}. Jobb extraherade: {jobs}.", cardCount, results.Count);
            foreach (var r in results.Take(5))
                log.LogInformation(" • {title} ({link}) id={id}", r.Title, r.Link, r.Id);
        }
    }
}
