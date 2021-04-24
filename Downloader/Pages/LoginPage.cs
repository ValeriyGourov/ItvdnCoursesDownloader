using System;

using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace Downloader.Pages
{
	/// <summary>
	/// Представление страницы авторизации для использования с Selenium WebDriver.
	/// </summary>
	internal sealed class LoginPage
	{
		private readonly IWebDriver _webDriver;
		private readonly Uri _loginPageUri;

		/// <summary>
		/// Основной конструктор.
		/// </summary>
		/// <param name="webDriver">Экземпляр веб-драйвера.</param>
		/// <param name="baseUri">Базовый адрес сайта.</param>
		public LoginPage(IWebDriver webDriver, Uri baseUri)
		{
			_webDriver = webDriver ?? throw new ArgumentNullException(nameof(webDriver));
			_loginPageUri = new Uri(baseUri, "ru/Account/Login") ?? throw new ArgumentNullException(nameof(baseUri));
		}

		/// <summary>
		/// Выполняет авторизацию пользователя на сайте.
		/// </summary>
		/// <param name="email">Логин пользователя.</param>
		/// <param name="password">Пароль пользователя.</param>
		public void Authorize(string email, string password)
		{
			_webDriver.Navigate().GoToUrl(_loginPageUri);

			// Заполнение поля "Логин".
			_webDriver
				.FindElement(By.Id("Email"))
				.SendKeys(email);
			// Заполнение поля "Пароль".
			_webDriver
				.FindElement(By.Id("Password"))
				.SendKeys(password);
			// Установка флажка "Запомнить меня".
			_webDriver
				.FindElement(By.ClassName("remember-me"))
				.Click();

			WebDriverWait wait = new(_webDriver, TimeSpan.FromMinutes(10));

			// Ожидание решения recaptcha.
			IWebElement iframe = _webDriver
				.FindElement(By.ClassName("g-recaptcha"))
				.FindElement(By.TagName("iframe"));
			_webDriver.SwitchTo().Frame(iframe);

			wait.Until(drv => drv
				.FindElement(By.Id("recaptcha-anchor"))
				.GetAttribute("aria-checked") == "true");

			_webDriver.SwitchTo().DefaultContent();

			// Нажатие кнопки "Войти".
			_webDriver
				.FindElement(By.ClassName("login-form-wrap"))
				.FindElement(By.ClassName("btn-orange-border-black-font"))
				.Click();

			// Ожидаем пока у кнопки авторизации заголовок станет равным адресу электронной почты. Это будет означать, что авторизация прошла успешно.
			wait.Until(drv => drv
				.FindElement(By.ClassName("top-header"))
				.FindElement(By.ClassName("cabinet-btn-title"))
				.Text.Equals(email, StringComparison.OrdinalIgnoreCase));
		}
	}
}
