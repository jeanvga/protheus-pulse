import { expect, test } from '@playwright/test'

test('autentica no modo demonstração e mostra o estado operacional', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByText('Bem-vindo de volta')).toBeVisible()
  await expect(page.getByText('Ambiente de demonstração')).toBeVisible()
  await page.getByRole('button', { name: 'Acessar dashboard' }).click()
  await expect(page.getByRole('heading', { name: 'Visão geral' })).toBeVisible()
  await expect(page.getByText('Modo demonstração')).toBeVisible()
  await expect(page.getByText('Job Fechamento', { exact: true })).toBeVisible()
})
