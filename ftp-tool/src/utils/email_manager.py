"""Email utility functions."""

import smtplib
from email.mime.text import MIMEText
from typing import Dict, List


class EmailManager:
    """Manages email sending functionality."""
    
    def __init__(self, settings: Dict = None):
        self.settings = settings or {}
    
    def update_settings(self, settings: Dict):
        """Update email settings."""
        self.settings.update(settings)
    
    def send_email(self, subject: str, body: str, to_addresses: List[str] = None) -> bool:
        """
        Send an email with the current settings.
        
        Args:
            subject: Email subject
            body: Email body text
            to_addresses: List of recipient addresses (optional, uses settings if not provided)
            
        Returns:
            True if successful, False otherwise
        """
        try:
            host = self.settings.get('smtp_host')
            port = int(self.settings.get('smtp_port', 587))
            user = self.settings.get('smtp_user')
            pwd = self.settings.get('smtp_pass')
            use_ssl = bool(self.settings.get('smtp_ssl', False))
            from_addr = self.settings.get('email_from')
            
            # Use provided addresses or fall back to settings
            if to_addresses:
                to_addrs = to_addresses
            else:
                to_addrs = [a.strip() for a in (self.settings.get('email_to') or '').split(';') if a.strip()]
            
            if not host or not from_addr or not to_addrs:
                raise ValueError('Email settings incomplete: missing host, from address, or recipients')
            
            # Create message
            msg = MIMEText(body)
            msg['Subject'] = subject
            msg['From'] = from_addr
            msg['To'] = ', '.join(to_addrs)
            
            # Connect to server
            if use_ssl:
                server = smtplib.SMTP_SSL(host, port, timeout=20)
            else:
                server = smtplib.SMTP(host, port, timeout=20)
            
            server.ehlo()
            
            # Start TLS if not using SSL
            if not use_ssl:
                try:
                    server.starttls()
                except Exception:
                    pass
            
            # Authenticate if credentials provided
            if user:
                server.login(user, pwd)
            
            # Send email
            server.sendmail(from_addr, to_addrs, msg.as_string())
            server.quit()
            
            return True
            
        except Exception as e:
            print(f"Failed to send email: {e}")
            return False
    
    def test_connection(self) -> bool:
        """Test the SMTP connection with current settings."""
        try:
            host = self.settings.get('smtp_host')
            port = int(self.settings.get('smtp_port', 587))
            user = self.settings.get('smtp_user')
            pwd = self.settings.get('smtp_pass')
            use_ssl = bool(self.settings.get('smtp_ssl', False))
            
            if not host:
                raise ValueError('SMTP host not configured')
            
            # Test connection
            if use_ssl:
                server = smtplib.SMTP_SSL(host, port, timeout=10)
            else:
                server = smtplib.SMTP(host, port, timeout=10)
            
            server.ehlo()
            
            if not use_ssl:
                try:
                    server.starttls()
                except Exception:
                    pass
            
            if user:
                server.login(user, pwd)
            
            server.quit()
            return True
            
        except Exception as e:
            print(f"SMTP connection test failed: {e}")
            return False
