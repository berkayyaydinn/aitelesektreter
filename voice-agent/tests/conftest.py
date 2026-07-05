"""Test ön-kurulumu. config.py import'ta zorunlu env ister — testler import etmeden önce ayarla."""
import os

os.environ.setdefault("BACKEND_BASE_URL", "http://backend.test")
os.environ.setdefault("INTERNAL_API_KEY", "test-key")
